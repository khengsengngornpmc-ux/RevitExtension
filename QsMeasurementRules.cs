using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
#if NETFRAMEWORK
using System.Web.Script.Serialization;
#else
using System.Text.Json.Serialization;
#endif

namespace CamboBIM.Revit2024.Addin
{
    internal sealed class QsMeasurementRulesProfile
    {
        public string ProfileName { get; set; } = "MHNK Measurement Rules - Cubicost TAS";
        public string Version { get; set; } = "1.0";
        public string UpdatedAtLocal { get; set; } = "";
        public List<QsMeasurementRuleRow> Rules { get; set; } = new List<QsMeasurementRuleRow>();

        public static QsMeasurementRulesProfile CreateDefault()
        {
            var profile = new QsMeasurementRulesProfile
            {
                ProfileName = "MHNK Measurement Rules - Cubicost TAS",
                Version = "1.0",
                UpdatedAtLocal = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
            };

            int n = 10;
            AddExcavationRules(profile.Rules, ref n);
            AddFoundationRules(profile.Rules, ref n);
            AddPileRules(profile.Rules, ref n);
            AddColumnRules(profile.Rules, ref n);
            AddWallRules(profile.Rules, ref n);
            AddBeamRules(profile.Rules, ref n);
            AddSlabRules(profile.Rules, ref n);
            AddSlabOpeningRules(profile.Rules, ref n);
            AddKerbAndOtherRules(profile.Rules, ref n);
            AddFinishRules(profile.Rules, ref n);
            AddExtendedStructureRules(profile.Rules, ref n);
            profile.Normalize();
            return profile;
        }

        public void Normalize()
        {
            if (string.IsNullOrWhiteSpace(ProfileName))
            {
                ProfileName = "MHNK Measurement Rules - Cubicost TAS";
            }

            if (string.IsNullOrWhiteSpace(Version))
            {
                Version = "1.0";
            }

            if (Rules == null)
            {
                Rules = new List<QsMeasurementRuleRow>();
            }

            MergeMissingDefaultRules();
            int order = 10;
            foreach (QsMeasurementRuleRow row in Rules)
            {
                if (row == null) continue;
                row.RefreshChoices();
                if (row.SortOrder <= 0)
                {
                    row.SortOrder = order;
                }

                order = Math.Max(order + 10, row.SortOrder + 10);
            }
        }

        public QsMeasurementRulesProfile Clone()
        {
            QsMeasurementRulesProfile clone = CamboBimJson.Deserialize<QsMeasurementRulesProfile>(CamboBimJson.Serialize(this));
            if (clone == null)
            {
                clone = CreateDefault();
            }

            clone.Normalize();
            return clone;
        }

        public QsMeasurementRuleRow FindRule(string code)
        {
            if (string.IsNullOrWhiteSpace(code) || Rules == null) return null;

            foreach (QsMeasurementRuleRow row in Rules)
            {
                if (row == null) continue;
                if (string.Equals(row.Code, code, StringComparison.OrdinalIgnoreCase))
                {
                    return row;
                }
            }

            return null;
        }

        public IEnumerable<QsMeasurementRuleRow> EnabledRules()
        {
            return (Rules ?? new List<QsMeasurementRuleRow>())
                .Where(r => r != null && r.IsEnabled);
        }

        internal static void RefreshChoices(QsMeasurementRuleRow row)
        {
            row?.RefreshChoices();
        }

        private void MergeMissingDefaultRules()
        {
            QsMeasurementRulesProfile defaults = CreateDefaultWithoutMerge();
            HashSet<string> existingCodes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (QsMeasurementRuleRow row in Rules)
            {
                if (row == null || string.IsNullOrWhiteSpace(row.Code)) continue;
                existingCodes.Add(row.Code);
            }

            foreach (QsMeasurementRuleRow row in defaults.Rules)
            {
                if (row == null || string.IsNullOrWhiteSpace(row.Code)) continue;
                if (existingCodes.Contains(row.Code)) continue;
                Rules.Add(row.Clone());
                existingCodes.Add(row.Code);
            }
        }

        private static QsMeasurementRulesProfile CreateDefaultWithoutMerge()
        {
            var profile = new QsMeasurementRulesProfile
            {
                ProfileName = "MHNK Measurement Rules - Cubicost TAS",
                Version = "1.0",
                UpdatedAtLocal = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)
            };

            int n = 10;
            AddExcavationRules(profile.Rules, ref n);
            AddFoundationRules(profile.Rules, ref n);
            AddPileRules(profile.Rules, ref n);
            AddColumnRules(profile.Rules, ref n);
            AddWallRules(profile.Rules, ref n);
            AddBeamRules(profile.Rules, ref n);
            AddSlabRules(profile.Rules, ref n);
            AddSlabOpeningRules(profile.Rules, ref n);
            AddKerbAndOtherRules(profile.Rules, ref n);
            AddFinishRules(profile.Rules, ref n);
            AddExtendedStructureRules(profile.Rules, ref n);
            foreach (QsMeasurementRuleRow row in profile.Rules)
            {
                row.RefreshChoices();
            }

            return profile;
        }

        private static void AddExcavationRules(List<QsMeasurementRuleRow> rows, ref int order)
        {
            ClassRule(rows, ref order, "Excavation", "EXC.CLASS.FLOOR", "Excavation-def", "Floor", "CBIM_BuildingLevel");
            ClassRule(rows, ref order, "Excavation", "EXC.CLASS.NAME", "Excavation-def", "Name", "Soil excavation direct-shape name");
            QtyRule(rows, ref order, "Excavation", "EXC.QTY.EXCAVATING", "Excavation-def", "Volume of excavating(m3)", "Soil excavation solid volume", "m3");
            QtyRule(rows, ref order, "Excavation", "EXC.QTY.BACKFILL", "Excavation-def", "Volume of backfilled soil(m3)", "Excavation volume - measured concrete displacement", "m3");
            QtyRule(rows, ref order, "Excavation", "EXC.QTY.NUMBER", "Excavation-def", "Number(pc)", "Element count", "pc");
        }

        private static void AddColumnRules(List<QsMeasurementRuleRow> rows, ref int order)
        {
            ClassRule(rows, ref order, "Column", "COL.CLASS.FLOOR", "Column-def", "Floor", "CBIM_BuildingLevel");
            ClassRule(rows, ref order, "Column", "COL.CLASS.MATERIAL", "Column-def", "Material", "In-situ Concrete");
            ClassRule(rows, ref order, "Column", "COL.CLASS.GRADE", "Column-def", "Concrete Grade", "Concrete Grade parameter");
            ClassRule(rows, ref order, "Column", "COL.CLASS.ENTITY", "Column-def", "Entity Type", "Column orientation/type classifier");
            QtyRule(rows, ref order, "Column", "COL.QTY.VOLUME", "Column-def", "Volume(m3)", "HOST_VOLUME_COMPUTED", "m3");
            QtyRule(rows, ref order, "Column", "COL.QTY.FORMWORK.BASIC", "Column-def", "Area of formwork for strutting high(0~1.5m)(m2)", "FWK.Col.Stage.Basic", "m2");
            StageRule(rows, ref order, "Column", "COL.QTY.FORMWORK.STAGE.1", "Column-def", "Area of formwork for strutting high(1.5~3m)(m2)", "FWK.Col.Stage.1", "m2");
            StageRule(rows, ref order, "Column", "COL.QTY.FORMWORK.STAGE.2", "Column-def", "Area of formwork for strutting high(3~4.5m)(m2)", "FWK.Col.Stage.2", "m2");
            StageRule(rows, ref order, "Column", "COL.QTY.FORMWORK.STAGE.3", "Column-def", "Area of formwork for strutting high(4.5~6m)(m2)", "FWK.Col.Stage.3", "m2");
            StageRule(rows, ref order, "Column", "COL.QTY.FORMWORK.STAGE.4", "Column-def", "Area of formwork for strutting high(6~7.5m)(m2)", "FWK.Col.Stage.4", "m2");
            QtyRule(rows, ref order, "Column", "COL.QTY.NUMBER", "Column-def", "Number(pc)", "Element count", "pc");
            QtyRule(rows, ref order, "Column", "COL.QTY.REBAR", "Column-def", "Weight of rebar(kg)", "Rebar.Weight", "kg");
            QtyRule(rows, ref order, "Column", "COL.QTY.GIRTH", "Column-def", "Girth(m)", "Section perimeter from width/depth", "m");
        }

        private static void AddWallRules(List<QsMeasurementRuleRow> rows, ref int order)
        {
            ClassRule(rows, ref order, "Wall", "WALL.CLASS.FLOOR", "Wall-def", "Floor", "CBIM_BuildingLevel");
            ClassRule(rows, ref order, "Wall", "WALL.CLASS.MATERIAL", "Wall-def", "Material", "In-situ Concrete");
            ClassRule(rows, ref order, "Wall", "WALL.CLASS.GRADE", "Wall-def", "Concrete Grade", "Concrete Grade parameter");
            ClassRule(rows, ref order, "Wall", "WALL.CLASS.ENTITY", "Wall-def", "Entity Type", "Wall orientation/type classifier");
            ClassRule(rows, ref order, "Wall", "WALL.CLASS.THICKNESS", "Wall-def", "Thickness", "Wall width/thickness");
            ClassRule(rows, ref order, "Wall", "WALL.CLASS.NAME", "Wall-def", "Name", "Element/type name");
            QtyRule(rows, ref order, "Wall", "WALL.QTY.VOLUME", "Wall-def", "Volume(m3)", "HOST_VOLUME_COMPUTED", "m3");
            QtyRule(rows, ref order, "Wall", "WALL.QTY.FORMWORK", "Wall-def", "Area of formwork(m2)", "CBIM_FormworkArea", "m2");
            StageRule(rows, ref order, "Wall", "WALL.QTY.EDGE.STAGE.0", "Wall-def", "Length of formwork to edge and break in stages(0~0.25m)(m)", "FWK.Wall.EdgeLength.Stage.0", "m");
            StageRule(rows, ref order, "Wall", "WALL.QTY.EDGE.STAGE.1", "Wall-def", "Length of formwork to edge and break in stages(0.25~0.5m)(m)", "FWK.Wall.EdgeLength.Stage.1", "m");
            StageRule(rows, ref order, "Wall", "WALL.QTY.EDGE.STAGE.2", "Wall-def", "Length of formwork to edge and break in stages(0.5~1m)(m)", "FWK.Wall.EdgeLength.Stage.2", "m");
            StageRule(rows, ref order, "Wall", "WALL.QTY.EDGE.AREA.STAGE.3", "Wall-def", "Area of formwork to edge and break in stages(>1m)(m2)", "FWK.Wall.EdgeArea.Stage.3", "m2");
            QtyRule(rows, ref order, "Wall", "WALL.QTY.AREA", "Wall-def", "Area(m2)", "HOST_AREA_COMPUTED", "m2");
            QtyRule(rows, ref order, "Wall", "WALL.QTY.REBAR", "Wall-def", "Weight of rebar(kg)", "Rebar.Weight", "kg");
            QtyRule(rows, ref order, "Wall", "WALL.QTY.NUMBER", "Wall-def", "Number(pc)", "Element count", "pc");
            QtyRule(rows, ref order, "Wall", "WALL.QTY.NET.LENGTH", "Wall-def", "Net length of wall(m)", "Wall location curve length", "m");
            QtyRule(rows, ref order, "Wall", "WALL.QTY.ORIGINAL.THICKNESS", "Wall-def", "Original thickness of wall(m)", "Wall width/thickness", "m");
            QtyRule(rows, ref order, "Wall", "WALL.QTY.ORIGINAL.HEIGHT", "Wall-def", "Original height of wall(m)", "FWK.Wall.OriginalHeight", "m");
            QtyRule(rows, ref order, "Wall", "WALL.QTY.ORIGINAL.LENGTH", "Wall-def", "Original length of wall(m)", "Wall location curve length", "m");
        }

        private static void AddBeamRules(List<QsMeasurementRuleRow> rows, ref int order)
        {
            ClassRule(rows, ref order, "Beam", "BEAM.CLASS.FLOOR", "Beam-def", "Floor", "CBIM_BuildingLevel");
            ClassRule(rows, ref order, "Beam", "BEAM.CLASS.MATERIAL", "Beam-def", "Material", "In-situ Concrete");
            ClassRule(rows, ref order, "Beam", "BEAM.CLASS.GRADE", "Beam-def", "Concrete Grade", "Concrete Grade parameter");
            ClassRule(rows, ref order, "Beam", "BEAM.CLASS.ENTITY", "Beam-def", "Entity Type", "Beam orientation/type classifier");
            QtyRule(rows, ref order, "Beam", "BEAM.QTY.VOLUME", "Beam-def", "Volume(m3)", "CBIM_Beam_Volume", "m3");
            QtyRule(rows, ref order, "Beam", "BEAM.QTY.FORMWORK.BASIC", "Beam-def", "Area of formwork(<=1.5m)(m2)", "FWK.Beam.Stage.Basic", "m2");
            StageRule(rows, ref order, "Beam", "BEAM.QTY.FORMWORK.STAGE.1", "Beam-def", "Area of formwork for strutting high(1.5~3m)(m2)", "FWK.Beam.Stage.1", "m2");
            StageRule(rows, ref order, "Beam", "BEAM.QTY.FORMWORK.STAGE.2", "Beam-def", "Area of formwork for strutting high(3~4.5m)(m2)", "FWK.Beam.Stage.2", "m2");
            StageRule(rows, ref order, "Beam", "BEAM.QTY.FORMWORK.STAGE.3", "Beam-def", "Area of formwork for strutting high(4.5~6m)(m2)", "FWK.Beam.Stage.3", "m2");
            StageRule(rows, ref order, "Beam", "BEAM.QTY.FORMWORK.STAGE.4", "Beam-def", "Area of formwork for strutting high(6~7.5m)(m2)", "FWK.Beam.Stage.4", "m2");
            QtyRule(rows, ref order, "Beam", "BEAM.QTY.GIRTH", "Beam-def", "Girth of section(m)", "2 x (CBIM_Beam_Width + CBIM_Beam_Depth)", "m");
            QtyRule(rows, ref order, "Beam", "BEAM.QTY.NET.LENGTH", "Beam-def", "Net length(m)", "CBIM_Beam_Length", "m");
            QtyRule(rows, ref order, "Beam", "BEAM.QTY.REBAR", "Beam-def", "Weight of rebar(kg)", "Rebar.Weight", "kg");
            QtyRule(rows, ref order, "Beam", "BEAM.QTY.NUMBER", "Beam-def", "Number(pc)", "Element count", "pc");
            QtyRule(rows, ref order, "Beam", "BEAM.QTY.AXIS.LENGTH", "Beam-def", "Length of axis(m)", "CBIM_Beam_Length", "m");
        }

        private static void AddSlabRules(List<QsMeasurementRuleRow> rows, ref int order)
        {
            ClassRule(rows, ref order, "Slab", "SLAB.CLASS.FLOOR", "In-situSlab-def", "Floor", "CBIM_BuildingLevel");
            ClassRule(rows, ref order, "Slab", "SLAB.CLASS.GRADE", "In-situSlab-def", "Concrete Grade", "Concrete Grade parameter");
            ClassRule(rows, ref order, "Slab", "SLAB.CLASS.ENTITY", "In-situSlab-def", "Entity Type", "Slab slope/type classifier");
            ClassRule(rows, ref order, "Slab", "SLAB.CLASS.THICKNESS", "In-situSlab-def", "Thickness", "Floor thickness");
            QtyRule(rows, ref order, "Slab", "SLAB.QTY.VOLUME", "In-situSlab-def", "Volume(m3)", "HOST_VOLUME_COMPUTED", "m3");
            QtyRule(rows, ref order, "Slab", "SLAB.QTY.AREA", "In-situSlab-def", "Area(m2)", "HOST_AREA_COMPUTED", "m2");
            QtyRule(rows, ref order, "Slab", "SLAB.QTY.SOFFIT.BASIC", "In-situSlab-def", "Area of formwork to soffit(<=1.5m)(m2)", "FWK.Floor.StrutBasic", "m2");
            StageRule(rows, ref order, "Slab", "SLAB.QTY.SOFFIT.STAGE.1", "In-situSlab-def", "Area of formwork to soffit for strutting high(1.5~3m)(m2)", "FWK.Floor.StrutStage.1", "m2");
            StageRule(rows, ref order, "Slab", "SLAB.QTY.SOFFIT.STAGE.2", "In-situSlab-def", "Area of formwork to soffit for strutting high(3~4.5m)(m2)", "FWK.Floor.StrutStage.2", "m2");
            StageRule(rows, ref order, "Slab", "SLAB.QTY.SOFFIT.STAGE.3", "In-situSlab-def", "Area of formwork to soffit for strutting high(4.5~6m)(m2)", "FWK.Floor.StrutStage.3", "m2");
            StageRule(rows, ref order, "Slab", "SLAB.QTY.SOFFIT.STAGE.4", "In-situSlab-def", "Area of formwork to soffit for strutting high(6~7.5m)(m2)", "FWK.Floor.StrutStage.4", "m2");
            QtyRule(rows, ref order, "Slab", "SLAB.QTY.EDGE.BREAK.AREA", "In-situSlab-def", "Area of formwork to edge and break of slab(m2)", "FWK.Floor.Sides", "m2");
            StageRule(rows, ref order, "Slab", "SLAB.QTY.EDGE.STAGE.1", "In-situSlab-def", "Length of formwork to edge and break of slab in stages(0~0.25m)(m)", "FWK.Floor.EdgeBreak.Lte250 as derived length", "m");
            StageRule(rows, ref order, "Slab", "SLAB.QTY.EDGE.STAGE.2", "In-situSlab-def", "Length of formwork to edge and break of slab in stages(0.25~0.5m)(m)", "FWK.Floor.EdgeBreak.Lte500 as derived length", "m");
            StageRule(rows, ref order, "Slab", "SLAB.QTY.EDGE.STAGE.3", "In-situSlab-def", "Length of formwork to edge and break of slab in stages(0.5~1m)(m)", "FWK.Floor.EdgeBreak.Lte1000 as derived length", "m");
            StageRule(rows, ref order, "Slab", "SLAB.QTY.EDGE.STAGE.4", "In-situSlab-def", "Area of formwork to edge and break of slab in stages(>1m)(m2)", "FWK.Floor.EdgeBreak.Over1000", "m2");
            QtyRule(rows, ref order, "Slab", "SLAB.QTY.PROJECTED.AREA", "In-situSlab-def", "Projected area(m2)", "HOST_AREA_COMPUTED projected", "m2");
            QtyRule(rows, ref order, "Slab", "SLAB.QTY.REBAR", "In-situSlab-def", "Weight of rebar(kg)", "Rebar.Weight", "kg");
            QtyRule(rows, ref order, "Slab", "SLAB.QTY.NUMBER", "In-situSlab-def", "Number(pc)", "Element count", "pc");
        }

        private static void AddSlabOpeningRules(List<QsMeasurementRuleRow> rows, ref int order)
        {
            ClassRule(rows, ref order, "Slab Opening", "SOPEN.CLASS.FLOOR", "SlabOpening-def", "Floor", "CBIM_BuildingLevel");
            ClassRule(rows, ref order, "Slab Opening", "SOPEN.CLASS.NAME", "SlabOpening-def", "Name", "Opening name/type when available");
            QtyRule(rows, ref order, "Slab Opening", "SOPEN.QTY.AREA", "SlabOpening-def", "Area(m2)", "FWK.Floor.Opening.Area", "m2");
            QtyRule(rows, ref order, "Slab Opening", "SOPEN.QTY.NUMBER", "SlabOpening-def", "Number(pc)", "FWK.Floor.Opening.Count", "pc");
            QtyRule(rows, ref order, "Slab Opening", "SOPEN.QTY.GIRTH", "SlabOpening-def", "Girth(m)", "FWK.Floor.Opening.Girth", "m");
        }

        private static void AddPileRules(List<QsMeasurementRuleRow> rows, ref int order)
        {
            ClassRule(rows, ref order, "Pile", "PILE.CLASS.FLOOR", "Pile-def", "Floor", "CBIM_BuildingLevel");
            ClassRule(rows, ref order, "Pile", "PILE.CLASS.NAME", "Pile-def", "Name", "Pile family/type name");
            QtyRule(rows, ref order, "Pile", "PILE.QTY.VOLUME", "Pile-def", "Volume(m3)", "HOST_VOLUME_COMPUTED", "m3");
            QtyRule(rows, ref order, "Pile", "PILE.QTY.EXCAVATING", "Pile-def", "Volume of excavating(m3)", "Pile excavation volume when available", "m3");
            QtyRule(rows, ref order, "Pile", "PILE.QTY.NUMBER", "Pile-def", "Number(pc)", "Element count", "pc");
            QtyRule(rows, ref order, "Pile", "PILE.QTY.LENGTH", "Pile-def", "Length(m)", "Pile depth/length", "m");
            QtyRule(rows, ref order, "Pile", "PILE.QTY.SECTION.AREA", "Pile-def", "Area of section(m2)", "Pile section area", "m2");
        }

        private static void AddFoundationRules(List<QsMeasurementRuleRow> rows, ref int order)
        {
            ClassRule(rows, ref order, "Foundation", "RAFT.CLASS.FLOOR", "RaftFoundation-def", "Floor", "CBIM_BuildingLevel");
            ClassRule(rows, ref order, "Foundation", "RAFT.CLASS.NAME", "RaftFoundation-def", "Name", "Element/type name");
            QtyRule(rows, ref order, "Foundation", "RAFT.QTY.VOLUME", "RaftFoundation-def", "Volume(m3)", "HOST_VOLUME_COMPUTED", "m3");
            StageRule(rows, ref order, "Foundation", "RAFT.QTY.SIDE.LENGTH.STAGE.1", "RaftFoundation-def", "Length of formwork to side in stages(0~0.25m)(m)", "FWK.Foun.SideLength.Stage.1", "m");
            StageRule(rows, ref order, "Foundation", "RAFT.QTY.SIDE.LENGTH.STAGE.2", "RaftFoundation-def", "Length of formwork to side in stages(0.25~0.5m)(m)", "FWK.Foun.SideLength.Stage.2", "m");
            QtyRule(rows, ref order, "Foundation", "RAFT.QTY.SOFFIT", "RaftFoundation-def", "Area of soffit(m2)", "Foundation soffit/projected underside", "m2");
            QtyRule(rows, ref order, "Foundation", "RAFT.QTY.VERTICAL", "RaftFoundation-def", "Area of vertical surface(m2)", "FWK.Foun.Sides", "m2");
            QtyRule(rows, ref order, "Foundation", "RAFT.QTY.PROJECTED", "RaftFoundation-def", "Projected area(m2)", "Foundation projected top area", "m2");

            StageRule(rows, ref order, "Foundation", "RAFT.QTY.SIDE.LENGTH.STAGE.3", "RaftFoundation-def", "Length of formwork to side in stages(0.5~1m)(m)", "FWK.Foun.SideLength.Stage.3", "m");
            StageRule(rows, ref order, "Foundation", "RAFT.QTY.SIDE.AREA.STAGE.4", "RaftFoundation-def", "Area of formwork to side in stages(>1m)(m2)", "FWK.Foun.SideArea.Stage.4", "m2");
            QtyRule(rows, ref order, "Foundation", "RAFT.QTY.REBAR", "RaftFoundation-def", "Weight of rebar(kg)", "Rebar.Weight", "kg");

            ClassRule(rows, ref order, "Foundation", "PAD.CLASS.FLOOR", "PileCap-def", "Floor", "CBIM_BuildingLevel");
            ClassRule(rows, ref order, "Foundation", "PAD.CLASS.NAME", "PileCap-def", "Name", "Element/type name");
            QtyRule(rows, ref order, "Foundation", "PAD.QTY.VOLUME", "PileCap-def", "Volume(m3)", "HOST_VOLUME_COMPUTED", "m3");
            StageRule(rows, ref order, "Foundation", "PAD.QTY.SIDE.LENGTH.STAGE.1", "PileCap-def", "Length of formwork to side in stages(0~0.25m)(m)", "FWK.Foun.SideLength.Stage.1", "m");
            StageRule(rows, ref order, "Foundation", "PAD.QTY.SIDE.LENGTH.STAGE.2", "PileCap-def", "Length of formwork to side in stages(0.25~0.5m)(m)", "FWK.Foun.SideLength.Stage.2", "m");
            StageRule(rows, ref order, "Foundation", "PAD.QTY.SIDE.LENGTH.STAGE.3", "PileCap-def", "Length of formwork to side in stages(0.5~1m)(m)", "FWK.Foun.SideLength.Stage.3", "m");
            StageRule(rows, ref order, "Foundation", "PAD.QTY.SIDE.AREA.STAGE.4", "PileCap-def", "Area of formwork to side in stages(>1m)(m2)", "FWK.Foun.SideArea.Stage.4", "m2");
            QtyRule(rows, ref order, "Foundation", "PAD.QTY.SOFFIT", "PileCap-def", "Area of soffit(m2)", "Foundation soffit/projected underside", "m2");
            QtyRule(rows, ref order, "Foundation", "PAD.QTY.SIDE", "PileCap-def", "Area of side(m2)", "FWK.Foun.Sides", "m2");
            QtyRule(rows, ref order, "Foundation", "PAD.QTY.TOP", "PileCap-def", "Area of top(m2)", "FWK.Foun.Top", "m2");
            QtyRule(rows, ref order, "Foundation", "PAD.QTY.NUMBER", "PileCap-def", "Number(pc)", "Element count", "pc");
            QtyRule(rows, ref order, "Foundation", "PAD.QTY.REBAR", "PileCap-def", "Weight of rebar(kg)", "Rebar.Weight", "kg");
        }

        private static void AddKerbAndOtherRules(List<QsMeasurementRuleRow> rows, ref int order)
        {
            ClassRule(rows, ref order, "Kerb", "KERB.CLASS.FLOOR", "Kerb-def", "Floor", "CBIM_BuildingLevel");
            ClassRule(rows, ref order, "Kerb", "KERB.CLASS.NAME", "Kerb-def", "Name", "Element/type name");
            QtyRule(rows, ref order, "Kerb", "KERB.QTY.VOLUME", "Kerb-def", "Volume(m3)", "HOST_VOLUME_COMPUTED", "m3");
            QtyRule(rows, ref order, "Kerb", "KERB.QTY.FORMWORK.SIDE", "Kerb-def", "Area of side formwork(m2)", "FWK.Kerb.Sides", "m2");
            QtyRule(rows, ref order, "Kerb", "KERB.QTY.FORMWORK.TOP", "Kerb-def", "Area of top formwork(m2)", "FWK.Kerb.Top", "m2");
            QtyRule(rows, ref order, "Kerb", "KERB.QTY.LENGTH", "Kerb-def", "Length(m)", "FWK.Kerb.Length", "m");
            QtyRule(rows, ref order, "Kerb", "KERB.QTY.NUMBER", "Kerb-def", "Number(pc)", "Element count", "pc");

            ClassRule(rows, ref order, "Others", "OTHER.CLASS.FLOOR", "Others-def", "Floor", "CBIM_BuildingLevel");
            ClassRule(rows, ref order, "Others", "OTHER.CLASS.NAME", "Others-def", "Name", "Element/type name");
            QtyRule(rows, ref order, "Others", "OTHER.QTY.VOLUME", "Others-def", "Volume(m3)", "HOST_VOLUME_COMPUTED", "m3");
            QtyRule(rows, ref order, "Others", "OTHER.QTY.FORMWORK.SIDE", "Others-def", "Area of side formwork(m2)", "FWK.Other.Sides", "m2");
            QtyRule(rows, ref order, "Others", "OTHER.QTY.FORMWORK.BOTTOM", "Others-def", "Area of bottom formwork(m2)", "FWK.Other.Bottom", "m2");
            QtyRule(rows, ref order, "Others", "OTHER.QTY.NUMBER", "Others-def", "Number(pc)", "Element count", "pc");
        }

        private static void AddFinishRules(List<QsMeasurementRuleRow> rows, ref int order)
        {
            AddFinishRules(rows, ref order, "Wall Finish", "WF", "WallFinish-def", "Wall finish area(m2)", "FIN.WF.Area");
            AddFinishRules(rows, ref order, "Ceiling Finish", "CF", "CeilingFinish-def", "Ceiling finish area(m2)", "FIN.CF.Area");
            AddFinishRules(rows, ref order, "Suspended Ceiling", "SC", "SuspendedCeiling-def", "Suspended ceiling area(m2)", "FIN.SC.Area");
            AddFinishRules(rows, ref order, "Floor Finish", "FF", "FloorFinish-def", "Floor finish area(m2)", "FIN.FF.Area");
            AddFinishRules(rows, ref order, "Waterproof", "WP", "Waterproof-def", "Waterproof area(m2)", "FIN.WP.Area");
            QtyRule(rows, ref order, "Waterproof", "WP.QTY.UPTURN", "Waterproof-def", "Area of waterproof upturn(m2)", "FIN.WP.Upturn", "m2");
        }

        private static void AddFinishRules(
            List<QsMeasurementRuleRow> rows,
            ref int order,
            string category,
            string prefix,
            string sheet,
            string areaHeader,
            string areaMapping)
        {
            ClassRule(rows, ref order, category, prefix + ".CLASS.FLOOR", sheet, "Floor", "CBIM_BuildingLevel");
            ClassRule(rows, ref order, category, prefix + ".CLASS.NAME", sheet, "Name", "Element/type name");
            if (string.Equals(prefix, "WF", StringComparison.OrdinalIgnoreCase))
            {
                ClassRule(rows, ref order, category, prefix + ".CLASS.ROOM", sheet, "Associated Room", "FIN.Room");
                ClassRule(rows, ref order, category, prefix + ".CLASS.PARENT", sheet, "Parent Entity Attribute Value", "-");
                QtyRule(rows, ref order, category, prefix + ".QTY.AREA", sheet, "Area of finish to wall finish(m2)", areaMapping, "m2");
                QtyRule(rows, ref order, category, prefix + ".QTY.SURFACE.OTHER", sheet, "Area of finish to surface of other material(m2)", areaMapping, "m2");
            }
            else
            {
                ClassRule(rows, ref order, category, prefix + ".CLASS.ROOM", sheet, "Room", "FIN.Room");
                QtyRule(rows, ref order, category, prefix + ".QTY.GROSS", sheet, "Gross area(m2)", "FIN." + prefix + ".Gross", "m2");
                QtyRule(rows, ref order, category, prefix + ".QTY.AREA", sheet, areaHeader, areaMapping, "m2");
            }
            DeductionRule(rows, ref order, category, prefix + ".QTY.OPENING", sheet, "Opening deduction area(m2)", "FIN." + prefix + ".OpeningDeduct", "m2");
            QtyRule(rows, ref order, category, prefix + ".QTY.RETURN", sheet, "Opening return/reveal area(m2)", "FIN." + prefix + ".Return", "m2");
            QtyRule(rows, ref order, category, prefix + ".QTY.NUMBER", sheet, "Number(pc)", "Element count", "pc");
        }

        private static void AddExtendedStructureRules(List<QsMeasurementRuleRow> rows, ref int order)
        {
            ClassRule(rows, ref order, "Lintel", "LINTEL.CLASS.FLOOR", "Lintel-def", "Floor", "CBIM_BuildingLevel");
            ClassRule(rows, ref order, "Lintel", "LINTEL.CLASS.NAME", "Lintel-def", "Name", "Element/type name");
            QtyRule(rows, ref order, "Lintel", "LINTEL.QTY.VOLUME", "Lintel-def", "Volume(m3)", "HOST_VOLUME_COMPUTED", "m3");
            QtyRule(rows, ref order, "Lintel", "LINTEL.QTY.FORMWORK.SIDE", "Lintel-def", "Area of side formwork(m2)", "FWK.Lintel.Sides", "m2");
            QtyRule(rows, ref order, "Lintel", "LINTEL.QTY.FORMWORK.BOTTOM", "Lintel-def", "Area of bottom formwork(m2)", "FWK.Lintel.Bottom", "m2");
            QtyRule(rows, ref order, "Lintel", "LINTEL.QTY.LENGTH", "Lintel-def", "Length(m)", "FWK.Lintel.Length", "m");
            QtyRule(rows, ref order, "Lintel", "LINTEL.QTY.NUMBER", "Lintel-def", "Number(pc)", "Element count", "pc");

            ClassRule(rows, ref order, "Drop Panel", "DROP.CLASS.FLOOR", "DropPanel-def", "Floor", "CBIM_BuildingLevel");
            ClassRule(rows, ref order, "Drop Panel", "DROP.CLASS.NAME", "DropPanel-def", "Name", "Element/type name");
            QtyRule(rows, ref order, "Drop Panel", "DROP.QTY.VOLUME", "DropPanel-def", "Volume(m3)", "HOST_VOLUME_COMPUTED", "m3");
            QtyRule(rows, ref order, "Drop Panel", "DROP.QTY.SOFFIT", "DropPanel-def", "Area of soffit formwork(m2)", "FWK.Drop.Soffit", "m2");
            StageRule(rows, ref order, "Drop Panel", "DROP.QTY.STRUT.STAGE.1", "DropPanel-def", "Area of formwork for strutting high stage 1(m2)", "FWK.Drop.Stage.1", "m2");
            QtyRule(rows, ref order, "Drop Panel", "DROP.QTY.NUMBER", "DropPanel-def", "Number(pc)", "Element count", "pc");

            ClassRule(rows, ref order, "Eave", "EAVE.CLASS.FLOOR", "Eave-def", "Floor", "CBIM_BuildingLevel");
            ClassRule(rows, ref order, "Eave", "EAVE.CLASS.NAME", "Eave-def", "Name", "Element/type name");
            QtyRule(rows, ref order, "Eave", "EAVE.QTY.BOTTOM", "Eave-def", "Area of bottom formwork(m2)", "FWK.Eave.Bottom", "m2");
            QtyRule(rows, ref order, "Eave", "EAVE.QTY.EDGE", "Eave-def", "Area of edge/break formwork(m2)", "FWK.Eave.Edge", "m2");
            QtyRule(rows, ref order, "Eave", "EAVE.QTY.NUMBER", "Eave-def", "Number(pc)", "Element count", "pc");

            ClassRule(rows, ref order, "Staircase", "STAIR.CLASS.FLOOR", "StraightFlight-def", "Floor", "CBIM_BuildingLevel");
            ClassRule(rows, ref order, "Staircase", "STAIR.CLASS.NAME", "StraightFlight-def", "Name", "Element/type name");
            QtyRule(rows, ref order, "Staircase", "STAIR.QTY.NUMBER", "StraightFlight-def", "Number(pc)", "Element count", "pc");
            QtyRule(rows, ref order, "Staircase", "STAIR.QTY.VOLUME", "StraightFlight-def", "Volume(m3)", "HOST_VOLUME_COMPUTED", "m3");
            QtyRule(rows, ref order, "Staircase", "STAIR.QTY.PAINTING", "StraightFlight-def", "Area of stair painting/formwork(m2)", "FWK.Stair.Painting", "m2");
            QtyRule(rows, ref order, "Staircase", "STAIR.QTY.PAINTING.SIDE", "StraightFlight-def", "Area of stair painting side/riser faces(m2)", "FWK.Stair.PaintingSide", "m2");
            QtyRule(rows, ref order, "Staircase", "STAIR.QTY.PAINTING.BOTTOM", "StraightFlight-def", "Area of stair painting bottom/soffit faces(m2)", "FWK.Stair.PaintingBottom", "m2");
            QtyRule(rows, ref order, "Staircase", "STAIR.QTY.STEPS", "StraightFlight-def", "Number of steps(pc)", "FWK.Stair.StepCount", "pc");
            QtyRule(rows, ref order, "Staircase", "STAIR.QTY.REBAR", "StraightFlight-def", "Weight of rebar(kg)", "Rebar.Weight", "kg");
        }

        private static void ClassRule(List<QsMeasurementRuleRow> rows, ref int order, string category, string code, string sheet, string header, string mapping)
        {
            Row(rows, ref order, category, code, "Classification Condition: " + header, sheet, mapping, "-", "Classification", true);
        }

        private static void QtyRule(List<QsMeasurementRuleRow> rows, ref int order, string category, string code, string sheet, string header, string mapping, string unit)
        {
            Row(rows, ref order, category, code, "Quantity: " + header, sheet, mapping, unit, "Quantity", true);
        }

        private static void StageRule(List<QsMeasurementRuleRow> rows, ref int order, string category, string code, string sheet, string header, string mapping, string unit)
        {
            Row(rows, ref order, category, code, "Stage Bucket: " + header, sheet, mapping, unit, "Stage Bucket", true);
        }

        private static void DeductionRule(List<QsMeasurementRuleRow> rows, ref int order, string category, string code, string sheet, string header, string mapping, string unit)
        {
            Row(rows, ref order, category, code, "Deduction: " + header, sheet, mapping, unit, "Deduction", true);
        }

        private static void Row(
            List<QsMeasurementRuleRow> rows,
            ref int order,
            string category,
            string code,
            string description,
            string tasSheet,
            string mapping,
            string unit,
            string method,
            bool enabled)
        {
            rows.Add(new QsMeasurementRuleRow
            {
                SortOrder = order,
                Category = category,
                Code = code,
                Description = description,
                Option = tasSheet,
                Value = mapping,
                Unit = unit,
                Method = method,
                IsEnabled = enabled
            });
            order += 10;
        }
    }

    internal sealed class QsMeasurementRuleRow : INotifyPropertyChanged
    {
        private bool _isEnabled;
        private string _category = "";
        private string _code = "";
        private string _description = "";
        private string _option = "";
        private string _value = "";
        private string _unit = "";
        private string _method = "";
        private List<string> _valueChoices = new List<string>();
        private List<string> _methodChoices = new List<string>();
        private int _displayIndex;
        private int _sortOrder;

        public event PropertyChangedEventHandler PropertyChanged;

        public bool IsEnabled { get { return _isEnabled; } set { Set(ref _isEnabled, value, "IsEnabled"); } }
        public string Category { get { return _category; } set { Set(ref _category, value ?? "", "Category"); } }
        public string Code { get { return _code; } set { Set(ref _code, value ?? "", "Code"); } }
        public string Description { get { return _description; } set { Set(ref _description, value ?? "", "Description"); } }
        public string Option
        {
            get { return _option; }
            set
            {
                if (Set(ref _option, value ?? "", "Option"))
                {
                    RaisePropertyChanged("OptionChoiceItems");
                }
            }
        }
        public string Unit { get { return _unit; } set { Set(ref _unit, value ?? "", "Unit"); } }
        public int SortOrder { get { return _sortOrder; } set { Set(ref _sortOrder, value, "SortOrder"); } }

        public string Value
        {
            get { return _value; }
            set
            {
                if (Set(ref _value, value ?? "", "Value"))
                {
                    RefreshChoices();
                }
            }
        }

        public string Method
        {
            get { return _method; }
            set
            {
                if (Set(ref _method, value ?? "", "Method"))
                {
                    RefreshChoices();
                }
            }
        }

        public List<string> ValueChoices
        {
            get { return _valueChoices; }
            set
            {
                if (Set(ref _valueChoices, value ?? new List<string>(), "ValueChoices"))
                {
                    RaisePropertyChanged("ValueChoiceItems");
                }
            }
        }

        public List<string> MethodChoices
        {
            get { return _methodChoices; }
            set
            {
                if (Set(ref _methodChoices, value ?? new List<string>(), "MethodChoices"))
                {
                    RaisePropertyChanged("MethodChoiceItems");
                }
            }
        }

#if NETFRAMEWORK
        [ScriptIgnore]
#else
        [JsonIgnore]
#endif
        public int DisplayIndex { get { return _displayIndex; } set { Set(ref _displayIndex, value, "DisplayIndex"); } }

#if NETFRAMEWORK
        [ScriptIgnore]
#else
        [JsonIgnore]
#endif
        public List<QsMeasurementChoiceItem> ValueChoiceItems
        {
            get { return BuildChoiceItems(ValueChoices, true); }
        }

#if NETFRAMEWORK
        [ScriptIgnore]
#else
        [JsonIgnore]
#endif
        public List<QsMeasurementChoiceItem> MethodChoiceItems
        {
            get { return BuildChoiceItems(MethodChoices, false); }
        }

#if NETFRAMEWORK
        [ScriptIgnore]
#else
        [JsonIgnore]
#endif
        public List<QsMeasurementChoiceItem> OptionChoiceItems
        {
            get { return BuildOptionChoiceItems(); }
        }

        public QsMeasurementRuleRow Clone()
        {
            return new QsMeasurementRuleRow
            {
                IsEnabled = IsEnabled,
                Category = Category,
                Code = Code,
                Description = Description,
                Option = Option,
                Value = Value,
                Unit = Unit,
                Method = Method,
                SortOrder = SortOrder,
                ValueChoices = new List<string>(ValueChoices ?? new List<string>()),
                MethodChoices = new List<string>(MethodChoices ?? new List<string>())
            };
        }

        public void RefreshChoices()
        {
            MethodChoices = BuildMethodChoices();
            ValueChoices = BuildValueChoices();
            RaisePropertyChanged("OptionChoiceItems");
        }

        private List<string> BuildMethodChoices()
        {
            var choices = new List<string>();
            AddChoice(choices, "Classification");
            AddChoice(choices, "Quantity");
            AddChoice(choices, "Deduction");
            AddChoice(choices, "Formula");
            AddChoice(choices, "Stage Bucket");
            AddChoice(choices, "Segmentation");
            AddChoice(choices, "Room Grouping");
            AddChoice(choices, "Parameter Map");
            AddChoice(choices, "Reference");
            AddChoice(choices, Method);
            return choices;
        }

        private List<string> BuildValueChoices()
        {
            var choices = new List<string>();
            AddChoices(
                choices,
                "CBIM_BuildingLevel",
                "HOST_VOLUME_COMPUTED",
                "HOST_AREA_COMPUTED",
                "CBIM_FormworkArea",
                "CBIM_QsFinishGrossArea",
                "CBIM_QsFinishArea",
                "CBIM_Beam_Volume",
                "CBIM_Beam_Length",
                "CBIM_Beam_Width",
                "CBIM_Beam_Depth",
                "CBIM_QsRuleCode",
                "CBIM_QsFormula",
                "CBIM_QsBreakdown",
                "FIN.Category",
                "FIN.Room",
                "Rebar.Weight",
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
                "FIN.WP.Upturn",
                "FWK.Col.Total",
                "FWK.Col.Sides",
                "FWK.Col.Stage.Basic",
                "FWK.Col.Stage.1",
                "FWK.Col.Stage.2",
                "FWK.Col.Stage.3",
                "FWK.Col.Stage.4",
                "FWK.Col.StrutHeight",
                "FWK.Col.StrutStage",
                "FWK.Wall.Total",
                "FWK.Wall.Sides",
                "FWK.Wall.EdgeLength.Stage.0",
                "FWK.Wall.EdgeLength.Stage.1",
                "FWK.Wall.EdgeLength.Stage.2",
                "FWK.Wall.EdgeArea.Stage.3",
                "FWK.Wall.OriginalHeight",
                "FWK.Beam.Stage.Basic",
                "FWK.Beam.Stage.1",
                "FWK.Beam.Stage.2",
                "FWK.Beam.Stage.3",
                "FWK.Beam.Stage.4",
                "FWK.Beam.StrutHeight",
                "FWK.Beam.StrutStage",
                "FWK.Floor.Total",
                "FWK.Floor.Bottom",
                "FWK.Floor.Sides",
                "FWK.Floor.StrutBasic",
                "FWK.Floor.StrutSoffit",
                "FWK.Floor.StrutStageArea",
                "FWK.Floor.StrutStage.1",
                "FWK.Floor.StrutStage.2",
                "FWK.Floor.StrutStage.3",
                "FWK.Floor.StrutStage.4",
                "FWK.Floor.EdgeBreak.Lte250",
                "FWK.Floor.Opening.Count",
                "FWK.Floor.Opening.Area",
                "FWK.Floor.Opening.Girth",
                "FWK.Foun.Total",
                "FWK.Foun.Sides",
                "FWK.Foun.Top",
                "FWK.Foun.SideLength.Total",
                "FWK.Foun.SideArea.Staged",
                "FWK.Foun.SideLength.Stage.1",
                "FWK.Foun.SideLength.Stage.2",
                "FWK.Foun.SideLength.Stage.3",
                "FWK.Foun.SideArea.Stage.1",
                "FWK.Foun.SideArea.Stage.2",
                "FWK.Foun.SideArea.Stage.3",
                "FWK.Kerb.Total",
                "FWK.Kerb.Sides",
                "FWK.Kerb.Top",
                "FWK.Kerb.Length",
                "FWK.Lintel.Sides",
                "FWK.Lintel.Bottom",
                "FWK.Lintel.Length",
                "FWK.Drop.Soffit",
                "FWK.Drop.Stage.1",
                "FWK.Eave.Bottom",
                "FWK.Eave.Edge",
                "FWK.Other.Total",
                "FWK.Other.Sides",
                "FWK.Other.Bottom",
                "FWK.Stair.Total",
                "FWK.Stair.Painting",
                "FWK.Stair.Sides",
                "FWK.Stair.PaintingSide",
                "FWK.Stair.PaintingBottom",
                "FWK.Stair.Bottom",
                "FWK.Stair.Top",
                "FWK.Stair.StepCount",
                "Element/type name",
                "Element length",
                "Material parameter",
                "Concrete Grade parameter",
                "Element count");
            AddChoice(choices, Value);
            return choices;
        }

        private List<QsMeasurementChoiceItem> BuildOptionChoiceItems()
        {
            var choices = new List<string>();
            AddChoices(
                choices,
                "Excavation-def",
                "Pile-def",
                "RaftFoundation-def",
                "PadFoundation-def",
                "PadFoundationUnit-def",
                "Column-def",
                "Wall-def",
                "Beam-def",
                "In-situSlab-def",
                "SlabOpening-def",
                "Kerb-def",
                "Others-def",
                "WallFinish-def",
                "CeilingFinish-def",
                "SuspendedCeiling-def",
                "FloorFinish-def",
                "Waterproof-def",
                "Lintel-def",
                "DropPanel-def",
                "Eave-def",
                "Staircase-def");
            AddChoice(choices, Option);

            var result = new List<QsMeasurementChoiceItem>();
            foreach (string choice in choices)
            {
                result.Add(new QsMeasurementChoiceItem(choice, DescribeOptionChoice(choice)));
            }

            return result;
        }

        private static List<QsMeasurementChoiceItem> BuildChoiceItems(IEnumerable<string> choices, bool forValue)
        {
            var result = new List<QsMeasurementChoiceItem>();
            if (choices == null) return result;

            foreach (string choice in choices)
            {
                if (string.IsNullOrWhiteSpace(choice)) continue;
                string value = choice.Trim();
                result.Add(new QsMeasurementChoiceItem(value, DescribeChoice(value, forValue)));
            }

            return result;
        }

        private static string DescribeChoice(string value, bool forValue)
        {
            if (!forValue)
            {
                if (string.Equals(value, "Classification", StringComparison.OrdinalIgnoreCase)) return "Cubicost classification condition column such as Floor, Name, Material, or Entity Type.";
                if (string.Equals(value, "Quantity", StringComparison.OrdinalIgnoreCase)) return "Cubicost quantity column mapped to a Revit/MHNK parameter or formula.";
                if (string.Equals(value, "Formula", StringComparison.OrdinalIgnoreCase)) return "Derived quantity calculated from one or more mapped parameters.";
                if (string.Equals(value, "Stage Bucket", StringComparison.OrdinalIgnoreCase)) return "Quantity split by height, support, or side-formwork range.";
                if (string.Equals(value, "Deduction", StringComparison.OrdinalIgnoreCase)) return "Quantity deducted by opening, void, or intersecting element conditions.";
                if (string.Equals(value, "Segmentation", StringComparison.OrdinalIgnoreCase)) return "Cubicost-style staged range rule, such as 0~0.25m, 0.25~0.5m, or high-support bands.";
                if (string.Equals(value, "Room Grouping", StringComparison.OrdinalIgnoreCase)) return "Classify or summarize finish quantities by room, level, type, or BOQ code.";
                if (string.Equals(value, "Parameter Map", StringComparison.OrdinalIgnoreCase)) return "Direct mapping from TAS header to a shared parameter.";
                if (string.Equals(value, "Reference", StringComparison.OrdinalIgnoreCase)) return "Rule is documented for TAS alignment but not yet calculated.";
                return "Custom measurement rule type.";
            }

            if (value.StartsWith("FIN.", StringComparison.OrdinalIgnoreCase)) return "MHNK finish quantity shared parameter generated by Calculate QS.";
            if (value.StartsWith("FWK.", StringComparison.OrdinalIgnoreCase)) return "MHNK QS shared parameter generated by Calculate QS.";
            if (value.StartsWith("CBIM_", StringComparison.OrdinalIgnoreCase)) return "MHNK shared or calculated parameter.";
            if (value.StartsWith("HOST_", StringComparison.OrdinalIgnoreCase)) return "Native Revit host quantity parameter.";
            if (string.Equals(value, "Element count", StringComparison.OrdinalIgnoreCase)) return "Count one measured Revit element in the current grouping.";
            if (string.Equals(value, "Element/type name", StringComparison.OrdinalIgnoreCase)) return "Use the Revit element name or type name for TAS classification.";
            if (string.Equals(value, "Material parameter", StringComparison.OrdinalIgnoreCase)) return "Use material, structural material, or configured concrete material source.";
            if (string.Equals(value, "Concrete Grade parameter", StringComparison.OrdinalIgnoreCase)) return "Use concrete grade/type parameter when present; otherwise leave configurable.";
            if (value.StartsWith("Future:", StringComparison.OrdinalIgnoreCase)) return "TAS rule target reserved for a later runtime calculation.";
            return "Rule mapping or formula expression.";
        }

        private static string DescribeOptionChoice(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return "";
            if (value.IndexOf("Foundation", StringComparison.OrdinalIgnoreCase) >= 0) return "Cubicost TAS foundation output/rule sheet.";
            if (value.IndexOf("Finish", StringComparison.OrdinalIgnoreCase) >= 0) return "Cubicost TAS architectural finish output/rule sheet.";
            if (value.IndexOf("Opening", StringComparison.OrdinalIgnoreCase) >= 0) return "Cubicost TAS opening quantity output/rule sheet.";
            if (value.IndexOf("Slab", StringComparison.OrdinalIgnoreCase) >= 0) return "Cubicost TAS slab output/rule sheet.";
            if (value.IndexOf("Beam", StringComparison.OrdinalIgnoreCase) >= 0) return "Cubicost TAS beam output/rule sheet.";
            if (value.IndexOf("Column", StringComparison.OrdinalIgnoreCase) >= 0) return "Cubicost TAS column output/rule sheet.";
            if (value.IndexOf("Wall", StringComparison.OrdinalIgnoreCase) >= 0) return "Cubicost TAS wall output/rule sheet.";
            if (value.IndexOf("Stair", StringComparison.OrdinalIgnoreCase) >= 0) return "Cubicost TAS staircase output/rule sheet.";
            if (value.IndexOf("Excavation", StringComparison.OrdinalIgnoreCase) >= 0) return "Cubicost TAS excavation output/rule sheet.";
            return "Cubicost TAS measurement rule/output sheet.";
        }

        private static void AddChoices(IList<string> choices, params string[] values)
        {
            foreach (string value in values ?? Array.Empty<string>())
            {
                AddChoice(choices, value);
            }
        }

        private static void AddChoice(IList<string> choices, string value)
        {
            if (choices == null || string.IsNullOrWhiteSpace(value)) return;
            string text = value.Trim();
            if (choices.Any(existing => string.Equals(existing, text, StringComparison.OrdinalIgnoreCase))) return;
            choices.Add(text);
        }

        private bool Set<T>(ref T field, T value, string propertyName)
        {
            if (EqualityComparer<T>.Default.Equals(field, value)) return false;
            field = value;
            RaisePropertyChanged(propertyName);
            return true;
        }

        private void RaisePropertyChanged(string propertyName)
        {
            PropertyChangedEventHandler handler = PropertyChanged;
            if (handler != null)
            {
                handler(this, new PropertyChangedEventArgs(propertyName));
            }
        }
    }
}
