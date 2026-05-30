using System;
using System.Collections.Generic;
using System.Linq;
using Autodesk.Revit.DB;
using Autodesk.Revit.DB.Structure;
using Autodesk.Revit.UI;
using Autodesk.Revit.UI.Selection;

namespace CamboBIM.Revit2024.Addin
{
    internal enum BoredPileRequestType
    {
        None,
        Initialize,
        PickImport,
        PickObjects,
        Generate
    }

    internal class BoredPileToolRequest
    {
        public BoredPileRequestType RequestType { get; set; } = BoredPileRequestType.None;

        public ElementId SelectedImportId { get; set; } = ElementId.InvalidElementId;
        public List<Reference> SelectedCadReferences { get; set; } = new List<Reference>();

        public bool UseLayerFilter { get; set; }
        public string SelectedLayerName { get; set; } = "";

        public ElementId BoredPileTypeId { get; set; } = ElementId.InvalidElementId;
        public ElementId SpunPileTypeId { get; set; } = ElementId.InvalidElementId;
        public ElementId SheetPileTypeId { get; set; } = ElementId.InvalidElementId;
        public ElementId BaseLevelId { get; set; } = ElementId.InvalidElementId;
    }

    internal class BoredPileToolExternalEventHandler : IExternalEventHandler
    {
        private BoredPileToolWindow _window;
        public BoredPileToolRequest Request { get; } = new BoredPileToolRequest();

        public void SetWindow(BoredPileToolWindow window)
        {
            _window = window;
        }

        public string GetName() => "MHNK BoredPileTool Handler";

        public void Execute(UIApplication app)
        {
            if (_window == null) return;

            UIDocument uidoc = app.ActiveUIDocument;
            if (uidoc == null) return;

            Document doc = uidoc.Document;

            try
            {
                switch (Request.RequestType)
                {
                    case BoredPileRequestType.Initialize:
                        InitializeUi(doc);
                        break;
                    case BoredPileRequestType.PickImport:
                        PickImport(uidoc, doc);
                        break;
                    case BoredPileRequestType.PickObjects:
                        PickObjects(uidoc, doc);
                        break;
                    case BoredPileRequestType.Generate:
                        Generate(doc);
                        break;
                }
            }
            catch (Autodesk.Revit.Exceptions.OperationCanceledException)
            {
                _window?.ShowStatus("Cancelled.");
            }
            catch (Exception ex)
            {
                TaskDialog.Show("MHNK", $"Error: {ex}");
            }
            finally
            {
                Request.RequestType = BoredPileRequestType.None;
            }
        }

        private void InitializeUi(Document doc)
        {
            var foundationTypes = new FilteredElementCollector(doc)
                .OfClass(typeof(FamilySymbol))
                .Cast<FamilySymbol>()
                .Where(fs => fs.Category != null &&
                             fs.Category.Id.Value == (int)BuiltInCategory.OST_StructuralFoundation)
                .OrderBy(fs => fs.FamilyName)
                .ThenBy(fs => fs.Name)
                .Select(fs => new BoredPileToolWindow.ComboItem(fs.Id, $"{fs.FamilyName} : {fs.Name}"))
                .ToList();

            var levels = new FilteredElementCollector(doc)
                .OfClass(typeof(Level))
                .Cast<Level>()
                .OrderBy(l => l.Elevation)
                .Select(l => new BoredPileToolWindow.ComboItem(l.Id, l.Name))
                .ToList();

            _window?.UpdateFoundationTypes(foundationTypes);
            _window?.UpdateLevels(levels);
        }

        private void PickImport(UIDocument uidoc, Document doc)
        {
            try
            {
                _window?.Hide();

                Reference r = uidoc.Selection.PickObject(
                    ObjectType.Element,
                    new ImportInstanceSelectionFilter(),
                    "Select AutoCAD Import (DWG) instance");

                ImportInstance cad = doc.GetElement(r) as ImportInstance;
                if (cad == null)
                {
                    _window?.ShowStatus("Selected element is not a DWG import.");
                    return;
                }

                Request.SelectedImportId = cad.Id;

                var layers = CollectCadLayers(doc, cad);
                _window?.UpdateImportName(cad.Name, layers);
            }
            finally
            {
                _window?.Show();
                _window?.Activate();
            }
        }

        private void PickObjects(UIDocument uidoc, Document doc)
        {
            if (Request.SelectedImportId == ElementId.InvalidElementId)
            {
                _window?.ShowStatus("Pick an AutoCAD import first.");
                return;
            }

            ImportInstance cad = doc.GetElement(Request.SelectedImportId) as ImportInstance;
            if (cad == null)
            {
                _window?.ShowStatus("Selected AutoCAD import is not available.");
                return;
            }

            try
            {
                _window?.Hide();

                IList<Reference> refs = uidoc.Selection.PickObjects(
                    ObjectType.PointOnElement,
                    new ImportInstanceReferenceFilter(),
                    "Select CAD objects in the import");

                Request.SelectedCadReferences = refs
                    .Where(r => r != null && r.ElementId == cad.Id)
                    .ToList();
            }
            finally
            {
                _window?.Show();
                _window?.Activate();
            }
        }

        private void Generate(Document doc)
        {
            if (Request.SelectedImportId == ElementId.InvalidElementId)
            {
                _window?.ShowStatus("Pick an AutoCAD import first.");
                return;
            }

            ImportInstance cad = doc.GetElement(Request.SelectedImportId) as ImportInstance;
            if (cad == null)
            {
                _window?.ShowStatus("Selected AutoCAD import is not available.");
                return;
            }

            FamilySymbol boredType = doc.GetElement(Request.BoredPileTypeId) as FamilySymbol;
            FamilySymbol spunType = doc.GetElement(Request.SpunPileTypeId) as FamilySymbol;
            FamilySymbol sheetType = doc.GetElement(Request.SheetPileTypeId) as FamilySymbol;
            Level baseLevel = doc.GetElement(Request.BaseLevelId) as Level;

            if (boredType == null || spunType == null || sheetType == null || baseLevel == null)
            {
                _window?.ShowStatus("Select pile families and base level.");
                return;
            }

            CadShapeHits hits = Request.UseLayerFilter
                ? ExtractShapesFromLayer(doc, cad, Request.SelectedLayerName)
                : ExtractShapesFromSelectedObjects(doc, cad, Request.SelectedCadReferences);

            if (hits.IsEmpty)
            {
                _window?.ShowStatus("No valid CAD geometry found.");
                return;
            }

            double tolFt = MmToFeet(10.0);
            List<XYZ> boredPts = DeduplicatePoints(hits.CircleCenters, tolFt);
            List<XYZ> spunPts = DeduplicatePoints(hits.BlockPoints, tolFt);
            List<Line> sheetLines = hits.Lines;

            int created = 0;
            using (Transaction t = new Transaction(doc, "MHNK - CAD to Bored Pile"))
            {
                t.Start();

                if (!boredType.IsActive) boredType.Activate();
                if (!spunType.IsActive) spunType.Activate();
                if (!sheetType.IsActive) sheetType.Activate();

                created += PlacePointFoundations(doc, boredType, baseLevel, boredPts);
                created += PlacePointFoundations(doc, spunType, baseLevel, spunPts);
                created += PlaceSheetPiles(doc, sheetType, baseLevel, sheetLines);

                t.Commit();
            }

            _window?.ShowStatus($"Created foundations: {created} (Bored: {boredPts.Count}, Spun: {spunPts.Count}, Sheet: {sheetLines.Count}).");
        }

        private CadShapeHits ExtractShapesFromSelectedObjects(Document doc, ImportInstance cad, List<Reference> refs)
        {
            var hits = new CadShapeHits();

            Transform importTrf = cad.GetTotalTransform() ?? Transform.Identity;

            foreach (Reference r in refs)
            {
                if (r == null) continue;
                GeometryObject go = cad.GetGeometryObjectFromReference(r);
                if (go == null) continue;

                ExtractShapesFromGeometryObject(doc, go, importTrf, "", hits);
            }

            return hits;
        }

        private CadShapeHits ExtractShapesFromLayer(Document doc, ImportInstance cad, string layerName)
        {
            var hits = new CadShapeHits();

            Options opt = new Options
            {
                ComputeReferences = false,
                DetailLevel = ViewDetailLevel.Fine,
                IncludeNonVisibleObjects = true
            };

            GeometryElement ge = cad.get_Geometry(opt);
            if (ge == null) return hits;

            Transform importTrf = cad.GetTotalTransform() ?? Transform.Identity;

            foreach (GeometryObject go in ge)
            {
                ExtractShapesFromGeometryObject(doc, go, importTrf, layerName, hits);
            }

            return hits;
        }

        private void ExtractShapesFromGeometryObject(Document doc, GeometryObject go, Transform trf, string layerFilter, CadShapeHits hits)
        {
            if (go == null) return;

            GeometryInstance gi = go as GeometryInstance;
            if (gi != null)
            {
                Transform instTrf = trf.Multiply(gi.Transform);
                hits.BlockPoints.Add(instTrf.Origin);

                GeometryElement instGeom = gi.GetInstanceGeometry();
                if (instGeom != null)
                {
                    foreach (GeometryObject igo in instGeom)
                    {
                        ExtractShapesFromGeometryObject(doc, igo, instTrf, layerFilter, hits);
                    }
                }
                return;
            }

            Curve c = go as Curve;
            if (c != null)
            {
                string layer = GetCadLayer(doc, go);
                if (!string.IsNullOrWhiteSpace(layerFilter) &&
                    !string.Equals(layerFilter, layer, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                Arc a = c as Arc;
                if (a != null && a.IsCyclic)
                {
                    hits.CircleCenters.Add(trf.OfPoint(a.Center));
                    return;
                }

                Line l = c as Line;
                if (l != null)
                {
                    hits.Lines.Add(Line.CreateBound(trf.OfPoint(l.GetEndPoint(0)), trf.OfPoint(l.GetEndPoint(1))));
                    return;
                }
            }

            PolyLine pl = go as PolyLine;
            if (pl != null)
            {
                string layer = GetCadLayer(doc, go);
                if (!string.IsNullOrWhiteSpace(layerFilter) &&
                    !string.Equals(layerFilter, layer, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                IList<XYZ> pts = pl.GetCoordinates();
                for (int i = 0; i < pts.Count - 1; i++)
                {
                    XYZ p0 = trf.OfPoint(pts[i]);
                    XYZ p1 = trf.OfPoint(pts[i + 1]);
                    hits.Lines.Add(Line.CreateBound(p0, p1));
                }
            }
        }

        private int PlacePointFoundations(Document doc, FamilySymbol symbol, Level baseLevel, List<XYZ> points)
        {
            int created = 0;
            foreach (XYZ p in points)
            {
                FamilyInstance fi = doc.Create.NewFamilyInstance(p, symbol, baseLevel, StructuralType.Footing);
                SetBaseLevel(fi, baseLevel);
                created++;
            }
            return created;
        }

        private int PlaceSheetPiles(Document doc, FamilySymbol symbol, Level baseLevel, List<Line> lines)
        {
            int created = 0;

            FamilyPlacementType placementType = symbol.Family.FamilyPlacementType;
            bool isCurveBased = placementType == FamilyPlacementType.CurveBased ||
                                placementType == FamilyPlacementType.CurveDrivenStructural;

            foreach (Line line in lines)
            {
                FamilyInstance fi;
                if (isCurveBased)
                {
                    fi = doc.Create.NewFamilyInstance(line, symbol, baseLevel, StructuralType.Footing);
                }
                else
                {
                    XYZ mid = (line.GetEndPoint(0) + line.GetEndPoint(1)) * 0.5;
                    fi = doc.Create.NewFamilyInstance(mid, symbol, baseLevel, StructuralType.Footing);
                }

                SetBaseLevel(fi, baseLevel);
                created++;
            }

            return created;
        }

        private static void SetBaseLevel(FamilyInstance fi, Level baseLevel)
        {
            Parameter pBase = fi.get_Parameter(BuiltInParameter.FAMILY_LEVEL_PARAM);
            if (pBase != null && !pBase.IsReadOnly)
                pBase.Set(baseLevel.Id);
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

        private static List<string> CollectCadLayers(Document doc, ImportInstance cad)
        {
            var layers = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            Options opt = new Options
            {
                ComputeReferences = false,
                DetailLevel = ViewDetailLevel.Fine,
                IncludeNonVisibleObjects = true
            };

            GeometryElement ge = cad.get_Geometry(opt);
            if (ge == null) return layers.OrderBy(l => l).ToList();

            foreach (GeometryObject go in ge)
            {
                CollectLayersFromGeom(doc, go, layers);
            }

            return layers.OrderBy(l => l).ToList();
        }

        private static void CollectLayersFromGeom(Document doc, GeometryObject go, HashSet<string> layers)
        {
            if (go == null) return;

            GeometryInstance gi = go as GeometryInstance;
            if (gi != null)
            {
                GeometryElement instGeom = gi.GetInstanceGeometry();
                if (instGeom != null)
                {
                    foreach (GeometryObject igo in instGeom)
                    {
                        CollectLayersFromGeom(doc, igo, layers);
                    }
                }
                return;
            }

            string layer = GetCadLayer(doc, go);
            if (!string.IsNullOrWhiteSpace(layer))
            {
                layers.Add(layer);
            }
        }

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

        private class ImportInstanceReferenceFilter : ISelectionFilter
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

        private class CadShapeHits
        {
            public List<XYZ> CircleCenters { get; } = new List<XYZ>();
            public List<XYZ> BlockPoints { get; } = new List<XYZ>();
            public List<Line> Lines { get; } = new List<Line>();

            public bool IsEmpty => CircleCenters.Count == 0 && BlockPoints.Count == 0 && Lines.Count == 0;
        }
    }
}
