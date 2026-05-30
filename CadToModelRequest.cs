using System;
using System.Collections.Generic;
using Autodesk.Revit.DB;

namespace CamboBIM.Revit2024.Addin
{
    internal enum CadToModelRequestType
    {
        None,
        Initialize,
        PickLink,
        PickObjects,
        EditFoundationSlabType,
        EditSelectedTypeProperties,
        RefreshFoundationSlabTypes,
        PickColumnSideline,
        PickColumnLabel,
        AutoDetectColumns,
        ClickIdentifyColumns,
        DragIdentifyColumns,
        PickColumnSchedule,
        AutoDetectWalls,
        PickWallSchedule,
        AutoDetectBeams,
        PickBeamSchedule,
        ApplyTasAttributesToSelection,
        GenerateGridlines,
        GenerateFoundations,
        GenerateLeanConcrete,
        GenerateSoilExcavation,
        GenerateSlabs,
        GenerateColumns,
        GenerateWalls,
        GenerateBeams,
        GeneratePiles,
        GenerateDetailLines,
        PickColumnRebarHost,
        GenerateColumnRebar,
        PickFoundationRebarHost,
        GenerateFoundationRebar,
        PickWallRebarHosts,
        GenerateWallRebar,
        PickBeamRebarHosts,
        PickBeamRebarSecondaryHosts,
        PickBeamRebarSupportHosts,
        GenerateBeamRebar,
        GenerateFormworkShopDrawings,
        GenerateRebarShopDrawings,
        GenerateConstructionShopSheets,
        GenerateSelectedElementShopViews,
        CalculateQs,
        ExportQsCsv,
        RefreshBoqTable,
        ViewQsExpression,
        HideQsFormworkShapes,
        DeleteQsFormworkShapes,
        RefreshLinksheetSchedules,
        RefreshLinksheetParameters,
        BuildLinksheetPreview,
        ApplyLinksheetPreviewEdits,
        ApplySiteProgress,
        ApplySiteProgressFromImport,
        RefreshSiteProgressSummary,
        SelectSiteProgressElements,
        ColorizeSiteProgressView,
        ClearSiteProgressColorView,
        SyncBoreyFloorData,
        SelectBoreyElements,
        ImportAdaptTendonProfiles,
        ImportAdaptCadDrawing,
        AutoJoinJoin,
        AutoJoinSwitch,
        AutoJoinUnjoin
    }

    internal enum QsScope
    {
        CurrentView,
        CurrentSelection,
        EntireModel
    }

    internal enum ShopDrawingGroupMode
    {
        LevelOnly,
        LevelAndCategory,
        LevelCategoryAndHost
    }

    internal enum SoilExcavationSourceMode
    {
        Foundation,
        Beam,
        Slab,
        All
    }

    internal enum AdaptTendonImportMode
    {
        Model3D,
        ProfileDetail
    }

    internal enum AdaptCadImportMode
    {
        LinkPreferred,
        ImportOnly
    }

    internal class ColumnRebarCustomLineTieSpec
    {
        public int X0GridIndex { get; set; }
        public int Y0GridIndex { get; set; }
        public int X1GridIndex { get; set; }
        public int Y1GridIndex { get; set; }
    }

    internal class ColumnRebarCustomRectTieSpec
    {
        public int X0GridIndex { get; set; }
        public int Y0GridIndex { get; set; }
        public int X1GridIndex { get; set; }
        public int Y1GridIndex { get; set; }
    }

    internal class ColumnRebarCustomMainBarSpec
    {
        public int GridXIndex { get; set; }
        public int GridYIndex { get; set; }
        public ElementId BarTypeId { get; set; } = ElementId.InvalidElementId;
    }

    internal enum FoundationNaviateRebarLayerKey
    {
        Top = 0,
        Bottom = 1
    }

    internal enum FoundationNaviateMainLayerDirection
    {
        LongitudinalBars = 0,
        TransversalBars = 1
    }

    internal enum FoundationNaviateReinforcementDisplayMode
    {
        Continuous = 0,
        LapLength = 1
    }

    internal enum FoundationNaviateQuantityInputMode
    {
        NumberOfBars = 0,
        Spacing = 1
    }

    internal enum FoundationNaviateOverlapInputMode
    {
        LengthMm = 0,
        DiameterMultiplier = 1
    }

    internal class FoundationNaviateLayerRequest
    {
        public FoundationNaviateRebarLayerKey LayerKey { get; set; } = FoundationNaviateRebarLayerKey.Top;
        public bool IsEnabled { get; set; } = true;
        public FoundationNaviateMainLayerDirection MainLayerDirection { get; set; } = FoundationNaviateMainLayerDirection.LongitudinalBars;
        public FoundationNaviateReinforcementDisplayMode ReinforcementDisplayMode { get; set; } = FoundationNaviateReinforcementDisplayMode.Continuous;
        public FoundationNaviateOverlapInputMode OverlapInputMode { get; set; } = FoundationNaviateOverlapInputMode.DiameterMultiplier;
        public int OverlapDiameterMultiplier { get; set; } = 40;
        public double OverlapLengthFt { get; set; }
        public ElementId BarTypeId { get; set; } = ElementId.InvalidElementId;
        public string BarTypeName { get; set; } = "";
        public string ShapeCode { get; set; } = "21";
        public string HookAtStart { get; set; } = "None";
        public string HookAtEnd { get; set; } = "None";
        public FoundationNaviateQuantityInputMode QuantityInputMode { get; set; } = FoundationNaviateQuantityInputMode.NumberOfBars;
        public int NumberOfBars { get; set; } = 2;
        public double SpacingFt { get; set; }

        // Compatibility bridge while migrating from spacing-based FOUNDATION input.
        public string XDirectionSpec { get; set; } = "";
        public string YDirectionSpec { get; set; } = "";
        public double XSpacingFt { get; set; }
        public double YSpacingFt { get; set; }
        public double LapLengthFt { get; set; }
    }

    internal class FoundationNaviateSideBarsRequest
    {
        public bool IsEnabled { get; set; } = true;
        public ElementId BarTypeId { get; set; } = ElementId.InvalidElementId;
        public string BarTypeName { get; set; } = "";
        public string TieSpec { get; set; } = "";
        public string ShapeCode { get; set; } = "21";
        public string HookAtStart { get; set; } = "None";
        public string HookAtEnd { get; set; } = "None";
        public double BottomTieSpacingFt { get; set; }
        public double MidTieSpacingFt { get; set; }
        public double TopTieSpacingFt { get; set; }
        public double BottomZoneLengthFt { get; set; }
        public double MidZoneLengthFt { get; set; }
        public double TopZoneLengthFt { get; set; }
    }

    internal class FoundationNaviateColumnStarterBarsRequest
    {
        public bool IsEnabled { get; set; } = true;
        public string MainSpec { get; set; } = "";
        public string XDirectionSpec { get; set; } = "";
        public string YDirectionSpec { get; set; } = "";
        public string TieSpec { get; set; } = "";
        public int BarsX { get; set; }
        public int BarsY { get; set; }
        public ElementId MainBarTypeId { get; set; } = ElementId.InvalidElementId;
        public ElementId TieBarTypeId { get; set; } = ElementId.InvalidElementId;
        public string MainBarTypeName { get; set; } = "";
        public string TieBarTypeName { get; set; } = "";
        public string TieShapeCode { get; set; } = "None";
        public string TieHookAtStart { get; set; } = "None";
        public string TieHookAtEnd { get; set; } = "None";
        public double TieSpacingFt { get; set; }
        public double TopLapLengthFt { get; set; }
        public int TopLapBarDiameterMultiplier { get; set; }
        public int LapBarsPercent { get; set; } = 100;
    }

    internal class FoundationNaviateRebarRequest
    {
        public bool IsEnabled { get; set; }
        public string SettingName { get; set; } = "<In Session>";
        public bool FollowPickedHostGeometry { get; set; } = true;
        public bool DeleteExistingRebar { get; set; } = true;
        public bool ShowDimensionsInPreview { get; set; } = true;
        public bool ShowBarMarkersInPreview { get; set; } = true;
        public double TopCoverFt { get; set; }
        public double BottomCoverFt { get; set; }
        public double SideCoverFt { get; set; }
        public FoundationNaviateLayerRequest TopBars { get; set; } =
            new FoundationNaviateLayerRequest { LayerKey = FoundationNaviateRebarLayerKey.Top };
        public FoundationNaviateLayerRequest BottomBars { get; set; } =
            new FoundationNaviateLayerRequest { LayerKey = FoundationNaviateRebarLayerKey.Bottom };
        public FoundationNaviateSideBarsRequest SideBars { get; set; } = new FoundationNaviateSideBarsRequest();
        public FoundationNaviateColumnStarterBarsRequest ColumnStarterBars { get; set; } = new FoundationNaviateColumnStarterBarsRequest();
    }

    internal class WallNaviateMainBarsRequest
    {
        public bool UseReinforcementMode1 { get; set; } = true;
        public int ReinforcementMode1OptionIndex { get; set; }
        public bool UseReinforcementMode2 { get; set; } = true;
        public int ReinforcementMode2OptionIndex { get; set; }

        public ElementId VerticalBarTypeId { get; set; } = ElementId.InvalidElementId;
        public string VerticalBarTypeName { get; set; } = "";
        public string VerticalHookTop { get; set; } = "None";
        public double VerticalSpacingFt { get; set; }

        public ElementId HorizontalBarTypeId { get; set; } = ElementId.InvalidElementId;
        public string HorizontalBarTypeName { get; set; } = "";
        public string HorizontalHookLeft { get; set; } = "None";
        public string HorizontalHookRight { get; set; } = "None";
        public double HorizontalSpacingFt { get; set; }

        public bool AutoSplitEnabled { get; set; }
        public double MaxBarLengthFt { get; set; }
        public bool UseFixedSplitLapLength { get; set; }
        public double FixedSplitLapLengthFt { get; set; }

        public bool CreateTieBars { get; set; }
        public ElementId TieBarTypeId { get; set; } = ElementId.InvalidElementId;
        public string TieBarTypeName { get; set; } = "";
        public double TieSpacingXFt { get; set; }
        public double TieSpacingZFt { get; set; }
    }

    internal class WallNaviateDowelRequest
    {
        public bool IsEnabled { get; set; } = true;
        public int PatternModeIndex { get; set; } = 0;
        public bool UseMainBarType { get; set; } = true;
        public ElementId BarTypeId { get; set; } = ElementId.InvalidElementId;
        public string BarTypeName { get; set; } = "";
        public int OverlapDiameterMultiplier { get; set; } = 40;
        public double OverlapLengthFt { get; set; }
    }

    internal class WallNaviateRebarRequest
    {
        public bool IsEnabled { get; set; } = true;
        public string SettingName { get; set; } = "<In Session>";
        public bool DeleteExistingRebar { get; set; }
        public List<ElementId> HostElementIds { get; set; } = new List<ElementId>();
        public WallNaviateMainBarsRequest MainBars { get; set; } = new WallNaviateMainBarsRequest();
        public WallNaviateDowelRequest TopDowels { get; set; } = new WallNaviateDowelRequest();
        public WallNaviateDowelRequest LeftDowels { get; set; } = new WallNaviateDowelRequest();
        public WallNaviateDowelRequest RightDowels { get; set; } = new WallNaviateDowelRequest();
    }

    internal class BeamNaviateMainBarsRequest
    {
        public ElementId UpperBarTypeId { get; set; } = ElementId.InvalidElementId;
        public string UpperBarTypeName { get; set; } = "";
        public ElementId UpperSecondLayerBarTypeId { get; set; } = ElementId.InvalidElementId;
        public string UpperSecondLayerBarTypeName { get; set; } = "";
        public int UpperLayerCount { get; set; } = 1;
        public int UpperBarsPerLayer { get; set; } = 2;
        public int UpperSecondBarsPerLayer { get; set; } = 2;

        public ElementId LowerBarTypeId { get; set; } = ElementId.InvalidElementId;
        public string LowerBarTypeName { get; set; } = "";
        public ElementId LowerSecondLayerBarTypeId { get; set; } = ElementId.InvalidElementId;
        public string LowerSecondLayerBarTypeName { get; set; } = "";
        public int LowerLayerCount { get; set; } = 1;
        public int LowerBarsPerLayer { get; set; } = 2;
        public int LowerSecondBarsPerLayer { get; set; } = 2;

        public bool CreateSideBars { get; set; }
        public ElementId SideBarTypeId { get; set; } = ElementId.InvalidElementId;
        public string SideBarTypeName { get; set; } = "";
        public int SideBarsPerFace { get; set; }
        public double SideAnchorLengthFt { get; set; }

        public bool AutoSplitMainBars { get; set; }
        public double MaxMainBarLengthFt { get; set; }
        public double LapLengthFt { get; set; }
        public double CoverFt { get; set; }
    }

    internal class BeamNaviateStirrupRequest
    {
        public bool IsEnabled { get; set; } = true;
        public ElementId BarTypeId { get; set; } = ElementId.InvalidElementId;
        public string BarTypeName { get; set; } = "";
        public string LayoutType { get; set; } = "L/4-L/2-L/4";
        public string ShapeMode { get; set; } = "Closed";
        public double StartSpacingFt { get; set; }
        public double MiddleSpacingFt { get; set; }
        public double EndSpacingFt { get; set; }
        public double StartZoneLengthFt { get; set; }
        public double EndZoneLengthFt { get; set; }
        public List<BeamNaviateStirrupSpanOverrideRequest> SpanOverrides { get; set; } = new List<BeamNaviateStirrupSpanOverrideRequest>();
    }

    internal class BeamNaviateStirrupSpanOverrideRequest
    {
        public int SpanIndex { get; set; }
        public double StartSpacingFt { get; set; }
        public double MiddleSpacingFt { get; set; }
        public double EndSpacingFt { get; set; }
        public double StartZoneLengthFt { get; set; }
        public double EndZoneLengthFt { get; set; }
    }

    internal class BeamNaviateAdditionalBarRequest
    {
        public bool IsEnabled { get; set; }
        public ElementId BarTypeId { get; set; } = ElementId.InvalidElementId;
        public string BarTypeName { get; set; } = "";
        public int BarCount { get; set; }
        public int LayerIndex { get; set; } = 1;
        public int StartGridIndex { get; set; } = 1;
        public int EndGridIndex { get; set; } = 1;
        public double StartLengthFt { get; set; }
        public double EndLengthFt { get; set; }
    }

    internal class BeamNaviateSecondaryBeamRequest
    {
        public bool IsEnabled { get; set; }
        public ElementId BarTypeId { get; set; } = ElementId.InvalidElementId;
        public string BarTypeName { get; set; } = "";
        public int BarCount { get; set; }
        public double StartLengthFt { get; set; }
        public double EndLengthFt { get; set; }
    }

    internal class BeamNaviateSpecialStirrupRequest
    {
        public bool IsEnabled { get; set; }
        public string Mode { get; set; } = "Tie Stirrup";
        public ElementId BarTypeId { get; set; } = ElementId.InvalidElementId;
        public string BarTypeName { get; set; } = "";
        public double SpacingFt { get; set; }
        public double ZoneLengthFt { get; set; }
        public List<BeamNaviateSpecialStirrupRowRequest> Rows { get; set; } = new List<BeamNaviateSpecialStirrupRowRequest>();
        public List<BeamNaviateCustomLineTieSpec> CustomLineTies { get; set; } = new List<BeamNaviateCustomLineTieSpec>();
        public List<BeamNaviateCustomRectTieSpec> CustomRectTies { get; set; } = new List<BeamNaviateCustomRectTieSpec>();
    }

    internal class BeamNaviateSpecialStirrupRowRequest
    {
        public int SectionIndex { get; set; } = 1;
        public int CageIndex { get; set; } = 1;
        public string Mode { get; set; } = "Tie Stirrup";
        public double SpacingFt { get; set; }
        public double StartZoneLengthFt { get; set; }
        public double EndZoneLengthFt { get; set; }
    }

    internal class BeamNaviateCustomLineTieSpec
    {
        public int SectionIndex { get; set; } = 1;
        public int X0GridIndex { get; set; }
        public int Y0GridIndex { get; set; }
        public int X1GridIndex { get; set; }
        public int Y1GridIndex { get; set; }
    }

    internal class BeamNaviateCustomRectTieSpec
    {
        public int SectionIndex { get; set; } = 1;
        public int X0GridIndex { get; set; }
        public int Y0GridIndex { get; set; }
        public int X1GridIndex { get; set; }
        public int Y1GridIndex { get; set; }
    }

    internal class BeamNaviateRebarRequest
    {
        public bool IsEnabled { get; set; } = true;
        public string SettingName { get; set; } = "<In Session>";
        public bool DeleteExistingRebar { get; set; } = true;
        public bool DeleteOnly { get; set; }
        public List<ElementId> HostElementIds { get; set; } = new List<ElementId>();
        public BeamNaviateMainBarsRequest MainBars { get; set; } = new BeamNaviateMainBarsRequest();
        public BeamNaviateStirrupRequest Stirrups { get; set; } = new BeamNaviateStirrupRequest();
        public BeamNaviateAdditionalBarRequest AdditionalBottomBars { get; set; } = new BeamNaviateAdditionalBarRequest();
        public BeamNaviateAdditionalBarRequest AdditionalTopBars { get; set; } = new BeamNaviateAdditionalBarRequest();
        public List<BeamNaviateAdditionalBarRequest> AdditionalBottomBarRows { get; set; } = new List<BeamNaviateAdditionalBarRequest>();
        public List<BeamNaviateAdditionalBarRequest> AdditionalTopBarRows { get; set; } = new List<BeamNaviateAdditionalBarRequest>();
        public BeamNaviateSecondaryBeamRequest SecondaryBeam { get; set; } = new BeamNaviateSecondaryBeamRequest();
        public BeamNaviateSpecialStirrupRequest SpecialStirrups { get; set; } = new BeamNaviateSpecialStirrupRequest();
    }

    internal class CadToModelRequest
    {
        public CadToModelRequestType RequestType { get; set; } = CadToModelRequestType.None;

        public ElementId SelectedLinkId { get; set; } = ElementId.InvalidElementId;
        public List<Reference> SelectedCadReferences { get; set; } = new List<Reference>();
        public List<Reference> SelectedColumnSidelineReferences { get; set; } = new List<Reference>();
        public List<Reference> SelectedColumnLabelReferences { get; set; } = new List<Reference>();
        public List<XYZ> ExplicitColumnPoints { get; set; } = new List<XYZ>();
        public bool HasSelectedColumnSidelineBox { get; set; }
        public XYZ SelectedColumnSidelineBoxMin { get; set; } = XYZ.Zero;
        public XYZ SelectedColumnSidelineBoxMax { get; set; } = XYZ.Zero;

        public bool UseLayerFilter { get; set; }
        public bool UseColorFilter { get; set; }
        public bool UseColumnLabelSelectionFilter { get; set; }
        public bool UseColumnLabelLayerFilter { get; set; }
        public bool UseColumnLabelColorFilter { get; set; }
        public string SelectedLayerName { get; set; } = "";
        public string SelectedLabelLayerName { get; set; } = "";
        public string SelectedColumnSidelineColorKey { get; set; } = "";
        public string SelectedColumnLabelColorKey { get; set; } = "";
        public string SelectedBoredLayerName { get; set; } = "";
        public string SelectedSpunLayerName { get; set; } = "";
        public string SelectedSheetLayerName { get; set; } = "";
        public double BoredPileTopOffsetFt { get; set; }

        public string SelectedFoundationSlabLayerName { get; set; } = "";
        public string SelectedFoundationIsolatedLayerName { get; set; } = "";
        public ElementId FoundationSlabTypeId { get; set; } = ElementId.InvalidElementId;
        public ElementId FoundationIsolatedTypeId { get; set; } = ElementId.InvalidElementId;
        public ElementId EditSelectedTypeId { get; set; } = ElementId.InvalidElementId;
        public List<BuiltInCategory> EditSelectedTypeCandidateCategories { get; set; } = new List<BuiltInCategory>();
        public string EditSelectedTypeDisplayName { get; set; } = "";
        public ElementId FoundationLevelId { get; set; } = ElementId.InvalidElementId;
        public double FoundationOffsetFt { get; set; }
        public ElementId LeanConcreteTypeId { get; set; } = ElementId.InvalidElementId;
        public ElementId LeanConcreteLevelId { get; set; } = ElementId.InvalidElementId;
        public double LeanConcreteSideOffsetFt { get; set; }
        public List<ElementId> SelectedLeanSourceElementIds { get; set; } = new List<ElementId>();
        public SoilExcavationSourceMode SoilExcavationSourceMode { get; set; } = SoilExcavationSourceMode.All;
        public double SoilExcavationTopOffsetFt { get; set; }
        public double SoilExcavationBaseOffsetFt { get; set; }
        public double SoilExcavationAngleDeg { get; set; }
        public List<ElementId> SelectedSoilExcavationSourceElementIds { get; set; } = new List<ElementId>();

        public bool UseCircles { get; set; } = true;
        public bool UseBlocks { get; set; } = true;
        public bool UseLineIntersections { get; set; }

        public bool TasAiConfigLoaded { get; set; }
        public string TasReferenceSummary { get; set; } = "";
        public double TasRdpEpsilonMm { get; set; } = 5.0;
        public double TasApproxPolylineEpsilon { get; set; } = 0.1;
        public bool TasRepairPolyline { get; set; } = true;
        public double TasCadLineParallelAngleEpsilonDeg { get; set; } = 5.0;
        public double TasCadLineSearchMaxDistanceMm { get; set; } = 200.0;
        public double TasShortLineMinLengthMm { get; set; } = 200.0;
        public string TasIdentificationOptionsSummary { get; set; } = "";
        public string TasColumnCodes { get; set; } = "";
        public string TasWallCodes { get; set; } = "";
        public string TasBeamCodes { get; set; } = "";
        public string TasBeamSizeIdentifyMode { get; set; } = "Width * Height";
        public double TasMaxColumnSidelineLengthMm { get; set; } = 1800.0;
        public double TasColumnLongShortRatioMax { get; set; } = 4.0;
        public double TasBeamDefaultHeightMm { get; set; } = 500.0;
        public double TasDefaultSlabThicknessMm { get; set; } = 120.0;
        public double TasLineMergeMaxDistanceMm { get; set; } = 400.0;
        public List<CadToModelTasAttributePayload> TasAttributes { get; set; } =
            new List<CadToModelTasAttributePayload>();

        public ElementId ColumnTypeId { get; set; } = ElementId.InvalidElementId;
        public ElementId WallTypeId { get; set; } = ElementId.InvalidElementId;
        public ElementId BeamTypeId { get; set; } = ElementId.InvalidElementId;
        public ElementId BaseLevelId { get; set; } = ElementId.InvalidElementId;
        public ElementId TopLevelId { get; set; } = ElementId.InvalidElementId;
        public bool CreateColumnTypesFromExcel { get; set; }
        public List<ColumnSectionSpec> ColumnSectionSpecs { get; set; } = new List<ColumnSectionSpec>();
        public bool CreateWallTypesFromExcel { get; set; }
        public List<WallSectionSpec> WallSectionSpecs { get; set; } = new List<WallSectionSpec>();
        public bool CreateBeamTypesFromExcel { get; set; }
        public List<BeamSectionSpec> BeamSectionSpecs { get; set; } = new List<BeamSectionSpec>();

        public ElementId BoredPileTypeId { get; set; } = ElementId.InvalidElementId;
        public ElementId SpunPileTypeId { get; set; } = ElementId.InvalidElementId;
        public ElementId SheetPileTypeId { get; set; } = ElementId.InvalidElementId;

        public ElementId SlabTypeId { get; set; } = ElementId.InvalidElementId;
        public ElementId SlabLevelId { get; set; } = ElementId.InvalidElementId;
        public string SelectedSlabLayerName { get; set; } = "";
        public bool UseSlabSectionSpecs { get; set; }
        public List<SlabSectionSpec> SlabSectionSpecs { get; set; } = new List<SlabSectionSpec>();

        public string SelectedBeamLabelLayerName { get; set; } = "";
        public ElementId DrawingLineStyleId { get; set; } = ElementId.InvalidElementId;
        public ElementId ColumnRebarHostElementId { get; set; } = ElementId.InvalidElementId;
        public List<ElementId> ColumnRebarHostElementIds { get; set; } = new List<ElementId>();
        public List<string> ColumnRebarSelectedFloorNames { get; set; } = new List<string>();
        public ElementId ColumnRebarMainBarTypeId { get; set; } = ElementId.InvalidElementId;
        public ElementId ColumnRebarTieBarTypeId { get; set; } = ElementId.InvalidElementId;
        public ElementId ColumnRebarOuterTieShapeId { get; set; } = ElementId.InvalidElementId;
        public ElementId ColumnRebarInnerTieShapeId { get; set; } = ElementId.InvalidElementId;
        public int ColumnRebarBarsX { get; set; } = 4;
        public int ColumnRebarBarsY { get; set; } = 4;
        public double ColumnRebarCoverFt { get; set; }
        public double ColumnRebarTieSpacingBottomFt { get; set; }
        public double ColumnRebarTieSpacingFt { get; set; }
        public double ColumnRebarTieSpacingTopFt { get; set; }
        public double ColumnRebarTieZoneBottomLengthFt { get; set; }
        public double ColumnRebarTieZoneMiddleLengthFt { get; set; }
        public double ColumnRebarTieZoneTopLengthFt { get; set; }
        public double ColumnRebarTieZoneBottomPercent { get; set; } = 33.3333;
        public double ColumnRebarTieZoneMiddlePercent { get; set; } = 33.3333;
        public double ColumnRebarTieZoneTopPercent { get; set; } = 33.3334;
        public double ColumnRebarBottomOffsetFt { get; set; }
        public double ColumnRebarTopOffsetFt { get; set; }
        public double ColumnRebarTopLapLengthFt { get; set; }
        public int ColumnRebarTopLapBarDiameterMultiplier { get; set; }
        public int ColumnRebarTopLapBarCoveragePercent { get; set; } = 100;
        public int ColumnRebarTieInnerLegsX { get; set; }
        public int ColumnRebarTieInnerLegsY { get; set; }
        public List<int> ColumnRebarTieSelectedXIndices { get; set; } = new List<int>();
        public List<int> ColumnRebarTieSelectedYIndices { get; set; } = new List<int>();
        public List<ColumnRebarCustomLineTieSpec> ColumnRebarCustomLineTies { get; set; } = new List<ColumnRebarCustomLineTieSpec>();
        public List<ColumnRebarCustomRectTieSpec> ColumnRebarCustomRectTies { get; set; } = new List<ColumnRebarCustomRectTieSpec>();
        public List<ColumnRebarCustomMainBarSpec> ColumnRebarCustomMainBars { get; set; } = new List<ColumnRebarCustomMainBarSpec>();
        public bool ColumnRebarCreateTies { get; set; } = true;
        public ElementId FoundationRebarHostElementId { get; set; } = ElementId.InvalidElementId;
        public ElementId FoundationRebarMainBarTypeId { get; set; } = ElementId.InvalidElementId;
        public ElementId FoundationRebarDistributionBarTypeId { get; set; } = ElementId.InvalidElementId;
        public ElementId FoundationRebarLinkBarTypeId { get; set; } = ElementId.InvalidElementId;
        public string FoundationRebarTypeModeText { get; set; } = "";
        public string FoundationRebarScopeModeText { get; set; } = "";
        public bool FoundationRebarApplyToSameType { get; set; }
        public bool FoundationRebarCreateTopBars { get; set; } = true;
        public bool FoundationRebarCreateBottomBars { get; set; } = true;
        public bool FoundationRebarCreateLinks { get; set; } = true;
        public bool FoundationRebarIncludeStarterBars { get; set; } = true;
        public bool FoundationRebarDeleteExisting { get; set; } = true;
        public double FoundationRebarLengthOverrideFt { get; set; }
        public double FoundationRebarWidthOverrideFt { get; set; }
        public double FoundationRebarThicknessOverrideFt { get; set; }
        public double FoundationRebarPedestalHeightFt { get; set; }
        public double FoundationRebarPedestalWidthFt { get; set; }
        public double FoundationRebarPedestalDepthFt { get; set; }
        public double FoundationRebarTopCoverFt { get; set; }
        public double FoundationRebarBottomCoverFt { get; set; }
        public double FoundationRebarSideCoverFt { get; set; }
        public double FoundationRebarTopSpacingXFt { get; set; }
        public double FoundationRebarTopSpacingYFt { get; set; }
        public double FoundationRebarBottomSpacingXFt { get; set; }
        public double FoundationRebarBottomSpacingYFt { get; set; }
        public double FoundationRebarBottomTieSpacingFt { get; set; }
        public double FoundationRebarMidTieSpacingFt { get; set; }
        public double FoundationRebarTopTieSpacingFt { get; set; }
        public double FoundationRebarTopLapLengthFt { get; set; }
        public double FoundationRebarBottomLapLengthFt { get; set; }
        public double FoundationRebarBottomZoneLengthFt { get; set; }
        public double FoundationRebarMidZoneLengthFt { get; set; }
        public double FoundationRebarTopZoneLengthFt { get; set; }
        public bool FoundationRebarUseNaviateDataModel { get; set; }
        public FoundationNaviateRebarRequest FoundationRebarNaviate { get; set; } = new FoundationNaviateRebarRequest();
        public List<ElementId> WallRebarHostElementIds { get; set; } = new List<ElementId>();
        public bool WallRebarUseNaviateDataModel { get; set; }
        public WallNaviateRebarRequest WallRebarNaviate { get; set; } = new WallNaviateRebarRequest();
        public List<ElementId> BeamRebarHostElementIds { get; set; } = new List<ElementId>();
        public List<ElementId> BeamRebarSecondaryElementIds { get; set; } = new List<ElementId>();
        public List<ElementId> BeamRebarSupportElementIds { get; set; } = new List<ElementId>();
        public bool BeamRebarUseNaviateDataModel { get; set; }
        public BeamNaviateRebarRequest BeamRebarNaviate { get; set; } = new BeamNaviateRebarRequest();

        public QsScope ShopDrawingsScope { get; set; } = QsScope.EntireModel;
        public bool ShopDrawingsIncludeFormwork { get; set; } = true;
        public bool ShopDrawingsIncludeRebar { get; set; } = true;
        public bool ShopDrawingsCreateSheets { get; set; } = true;
        public bool ShopDrawingsReplaceExisting { get; set; } = true;
        public int ShopDrawingsViewScale { get; set; } = 50;
        public ElementId ShopDrawingsTitleBlockTypeId { get; set; } = ElementId.InvalidElementId;
        public string ShopDrawingsIssueText { get; set; } = "FOR CONSTRUCTION REVIEW";
        public string ShopDrawingsFormworkSheetPrefix { get; set; } = "MHNK-FWK";
        public string ShopDrawingsRebarSheetPrefix { get; set; } = "MHNK-RBR";
        public string ShopDrawingsIndexSheetPrefix { get; set; } = "MHNK-SD-INDEX";
        public string ShopDrawingsDetailSheetPrefix { get; set; } = "MHNK-DET";
        public int ShopDrawingsMaxMarksPerSheet { get; set; } = 32;
        public ShopDrawingGroupMode ShopDrawingsGroupMode { get; set; } = ShopDrawingGroupMode.LevelAndCategory;
        public string ShopDrawingsPackageName { get; set; } = "SHOP DRAWING PACKAGE";
        public string ShopDrawingsDiscipline { get; set; } = "STRUCTURAL";
        public string ShopDrawingsRevision { get; set; } = "R0";
        public string ShopDrawingsPreparedBy { get; set; } = "";
        public string ShopDrawingsCheckedBy { get; set; } = "";
        public string ShopDrawingsApprovedBy { get; set; } = "";

        public QsScope QsScope { get; set; } = QsScope.CurrentView;
        public bool QsIncludeStructuralFraming { get; set; } = true;
        public bool QsIncludeStructuralColumn { get; set; } = true;
        public bool QsIncludeStructuralFloor { get; set; } = true;
        public bool QsIncludeStructuralWall { get; set; } = true;
        public bool QsIncludeStructuralStair { get; set; } = true;
        public bool QsIncludeFoundation { get; set; } = true;
        public bool QsBeamIncludeBottom { get; set; } = true;
        public bool QsColumnSubtractBeam { get; set; } = false;
        public bool QsColumnSubtractBeamGreaterOrEqual { get; set; } = false;
        public bool QsFloorIncludeBottom { get; set; } = true;
        public bool QsFloorSubtractBeam { get; set; } = true;
        public bool QsFloorSubtractFoundation { get; set; } = true;
        public bool QsFloorSubtractOthers { get; set; } = true;
        public bool QsWallIncludeOpeningBottom { get; set; } = true;
        public bool QsStairIncludeTop { get; set; } = false;
        public bool QsStairSubtractBeam { get; set; } = true;
        public bool QsStairSubtractOthers { get; set; } = true;
        public bool QsCreateFormworkShape { get; set; } = true;
        public bool QsFoundationIncludeTop { get; set; } = true;
        public QsMeasurementSettingsProfile QsMeasurementSettingsProfile { get; set; } = QsMeasurementSettingsProfile.CreateDefault();
        public QsMeasurementRulesProfile QsMeasurementRulesProfile { get; set; } = QsMeasurementRulesProfile.CreateDefault();
        public string QsSharedParameterFilePath { get; set; } = "";
        public string QsSharedParameterGroupName { get; set; } = "CBIM-QS";
        public string QsExportCsvPath { get; set; } = "";
        public bool QsIncludeSoilExcavationInBoq { get; set; } = true;
        public bool QsIncludeSoilBackfilledInBoq { get; set; } = true;
        public bool QsSoilBackfilledSubtractStructures { get; set; } = true;
        public QsScope BoqScope { get; set; } = QsScope.EntireModel;
        public QsScope LinksheetScope { get; set; } = QsScope.EntireModel;
        public List<string> LinksheetSelectedScheduleNames { get; set; } = new List<string>();
        public List<string> LinksheetSelectedParameterKeys { get; set; } = new List<string>();
        public List<LinksheetCellEdit> LinksheetEditedCells { get; set; } = new List<LinksheetCellEdit>();
        public double SiteProgressPercent { get; set; }
        public double SiteProgressReinforcementPercent { get; set; }
        public double SiteProgressFormworkPercent { get; set; }
        public double SiteProgressVolumePercent { get; set; }
        public bool SiteProgressIsAddMode { get; set; }
        public QsScope SiteProgressScope { get; set; } = QsScope.EntireModel;
        public string SiteProgressStructureFilter { get; set; } = "All";
        public string SiteProgressBuildingLevelFilter { get; set; } = "All";
        public List<int> SiteProgressTargetElementIds { get; set; } = new List<int>();
        public bool SiteProgressFocusFilteredView { get; set; } = false;
        public List<SiteProgressImportRowPayload> SiteProgressImportRows { get; set; } = new List<SiteProgressImportRowPayload>();
        public AdaptTendonImportMode AdaptTendonImportMode { get; set; } = AdaptTendonImportMode.Model3D;
        public string AdaptTendonSourcePath { get; set; } = "";
        public string AdaptCadSourcePath { get; set; } = "";
        public AdaptCadImportMode AdaptCadImportMode { get; set; } = AdaptCadImportMode.LinkPreferred;
        public List<AdaptTendonProfileSegmentPayload> AdaptTendonProfileSegments { get; set; } =
            new List<AdaptTendonProfileSegmentPayload>();

        public string BoreySharedParameterFilePath { get; set; } = "";
        public string BoreySharedParameterGroupName { get; set; } = "CBIM-BOREY";
        public bool BoreyEnsureSharedParameters { get; set; }
        public bool BoreyApplyGridChanges { get; set; }
        public string BoreyLogicalTableName { get; set; } = "";
        public List<BoreyGridRowPayload> BoreyRows { get; set; } = new List<BoreyGridRowPayload>();

        public bool AutoJoinUseActiveView { get; set; } = true;
        public List<Autodesk.Revit.DB.BuiltInCategory> AutoJoinCategories { get; set; } = new List<Autodesk.Revit.DB.BuiltInCategory>();

        public void ClearSelection()
        {
            SelectedCadReferences.Clear();
            SelectedColumnSidelineReferences.Clear();
            SelectedColumnLabelReferences.Clear();
            ExplicitColumnPoints.Clear();
            SelectedColumnSidelineColorKey = "";
            SelectedColumnLabelColorKey = "";
            UseColumnLabelSelectionFilter = false;
            UseColumnLabelLayerFilter = false;
            UseColumnLabelColorFilter = false;
            HasSelectedColumnSidelineBox = false;
            SelectedColumnSidelineBoxMin = XYZ.Zero;
            SelectedColumnSidelineBoxMax = XYZ.Zero;
            SelectedLeanSourceElementIds.Clear();
            SelectedSoilExcavationSourceElementIds.Clear();
        }
    }

    internal class BoreyGridRowPayload
    {
        public Dictionary<string, string> Values { get; set; } =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    }

    internal class SiteProgressImportRowPayload
    {
        public string StructureElement { get; set; } = "";
        public string BuildingLevel { get; set; } = "";
        public string TypeName { get; set; } = "";
        public double? ReinforcementPercent { get; set; }
        public double? FormworkPercent { get; set; }
        public double? VolumePercent { get; set; }
    }

    internal class AdaptTendonProfileSegmentPayload
    {
        public string ProfileName { get; set; } = "";
        public string TendonName { get; set; } = "";
        public string SourceLabel { get; set; } = "";
        public double X0Ft { get; set; }
        public double Y0Ft { get; set; }
        public double Z0Ft { get; set; }
        public double X1Ft { get; set; }
        public double Y1Ft { get; set; }
        public double Z1Ft { get; set; }
    }

    internal class CadToModelTasAttributePayload
    {
        public string Category { get; set; } = "";
        public string ElementLabel { get; set; } = "";
        public string ToolName { get; set; } = "";
        public string GroupKey { get; set; } = "";
        public string Attribute { get; set; } = "";
        public string Value { get; set; } = "";
        public bool Add { get; set; }
    }

    internal class ColumnSectionSpec
    {
        public string Name { get; set; } = "";
        public double WidthMm { get; set; }
        public double LengthMm { get; set; }
    }

    internal class WallSectionSpec
    {
        public string Name { get; set; } = "";
        public double WidthMm { get; set; }
    }

    internal class BeamSectionSpec
    {
        public string Name { get; set; } = "";
        public double WidthMm { get; set; }
        public double DepthMm { get; set; }
    }

    internal class SlabSectionSpec
    {
        public string Name { get; set; } = "";
        public double ThicknessMm { get; set; }
    }
}
