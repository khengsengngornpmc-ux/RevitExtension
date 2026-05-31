# ADAPT-Builder To Revit Import

`DRAWING PT` can import ADAPT tendon geometry directly from supported `.adm` files without opening ADAPT-Builder. The direct `.adm` parser reads tendon profile/control-point records from the project file in read-only mode and creates visible Revit tendon geometry.

This matches the broader product direction reinforced by the RISA workflow reference `Designing Post-Tensioning using Autodesk Revit, ADAPT-Builder and ADAPT-PT/RC`, where Revit acts as the final environment for PT coordination and documentation after ADAPT-side design work.

Use `MHNK > STR > DRAWING PT` for the dedicated ADAPT/PT workflow. The button opens the ADAPT import picker directly.

If an `.adm` file is selected, the add-in first tries direct tendon profile import from the `.adm`. If direct profile geometry is not found, it looks for a nearby DWG/DXF export from that ADAPT project and offers to import it. If neither path is available, it stops and asks the user to select an ADAPT-exported DWG/DXF or tendon/profile table. `DRAWING PT` does not launch ADAPT-Builder.

The tool remembers the last ADAPT folder and project between Revit sessions. On the next `DRAWING PT` click, it can offer the latest DWG/DXF export from the previous ADAPT project before asking the user to browse again.

For DWG/DXF files, `DRAWING PT` asks whether to link the export or import it into the model. Link is recommended because the source remains external and easier to refresh/manage; import is available when an embedded CAD copy is required. The last selected mode is remembered between Revit sessions.

If the same ADAPT CAD export is already in the Revit model, `DRAWING PT` asks whether to reuse the existing CAD source or import another copy. Each CAD import/reuse attempt is logged under `%APPDATA%\MHNK\RevitExtension\DRAWING_PT\Reports`.

After a DWG/DXF import or reuse, the tool analyzes CAD layer names and reports likely PT/tendon/profile layers in the status message and audit log. If no PT-specific layer name is obvious, it reports the top populated CAD layers so the user can inspect the export quickly.

When readable PT candidate layers are found, `DRAWING PT` now also builds native Revit drafting preview views from those CAD lines, detects connected tendon-like chains, assigns CAD-based or generated PT marks, builds PT takeoff rows, and packages the result onto sheets with an index sheet and takeoff sheet set when a title block is available.

## Verified Local Install

- ADAPT-Builder 2018 exists at `C:\Program Files (x86)\ADAPT\ADAPT-Builder 2018\builder.exe`, but `DRAWING PT` does not use or open this executable for `.adm` import.
- ADAPT-Builder 2019 is also installed on this PC.
- `.adm` files are associated with ADAPT-Builder 2018 on this machine, but the Revit add-in reads the selected file path directly.

The sample file `C:\Users\PC\Desktop\New folder\6950022_1F-12STRANDS_round_duct.adm` was inspected as a binary ADAPT project file. Its direct tendon profile/control-point records were readable without starting ADAPT-Builder.

## Recommended Workflows

### Direct `.adm` Tendon Geometry

1. In Revit, open `MHNK > STR > DRAWING PT`.
2. Select the ADAPT `.adm` model.
3. If tendon profile records are found, the add-in creates `MHNK ADAPT Tendon` solids and centerlines in Revit for 3D tendon imports, generates tendon-profile drafting views for review, builds PT takeoff rows from the imported tendon metadata, and packages those views onto profile sheets plus takeoff sheets when a title block is available.
4. For 3D direct imports, the add-in also creates a dedicated PT model review sheet so the tendon geometry can be checked in an isometric Revit view.
5. Check the status bar for the number of imported profiles, points, segments, and inferred units.

The direct parser uses ADAPT profile/control-point blocks rather than the flat plan centerline. ADAPT X/Z are treated as plan/station axes, and ADAPT Y is treated as the Revit elevation axis.

### DWG/DXF Fallback

Use this when direct `.adm` import does not find readable tendon geometry or when you prefer the ADAPT drawing appearance.

1. Export the tendon plan using ADAPT's DWG/DXF export.
2. In Revit, click `MHNK > STR > DRAWING PT` and select the DWG/DXF.
3. The add-in links the drawing at the project origin, selects it as the CAD2MODEL source, and loads the CAD layers.
4. If PT candidate layers are detected, the add-in builds native Revit drafting summary views from those layer lines, groups connected chains, adds PT marks and summaries, creates isolated chain-detail views for the strongest chains, builds material-takeoff rows from the detected chains, creates a dedicated CAD PT model review sheet, and creates a DWG/DXF PT package sheet set with takeoff sheets when possible.
5. Use existing CAD2MODEL tools to pick CAD geometry or layers.

### Tendon/Profile Table

1. Export or copy a tendon/profile point table from ADAPT/report output to CSV, TXT, TSV, XLSX, XLSM, or XLS.
2. In Revit, click `MHNK > STR > DRAWING PT`.
3. Select the exported table.

The importer creates:

- A selected CAD2MODEL source when the file is ADAPT-exported DWG/DXF.
- CAD instance comments with ADAPT PT source metadata after a DWG/DXF link/import.
- Duplicate detection for previously linked/imported ADAPT PT CAD exports.
- PT/tendon/profile layer candidates from the imported DWG/DXF.
- Native drafting preview views from the strongest PT CAD layer candidates.
- Connected chain detection plus CAD-based or generated PT marks in the DWG/DXF preview package.
- Chain-detail drafting views for the strongest detected tendon-like paths.
- DWG/DXF PT package sheets plus an index sheet when a title block is available.
- PT takeoff drafting sheets for both direct ADAPT import and DWG/DXF chain detection, with automatic paging when the takeoff exceeds one sheet.
- PT model review sheets for both direct ADAPT 3D import and DWG/DXF CAD import when a title block is available.
- 3D tendon solids plus centerlines when the table contains `X`, `Y`, and `Z` style columns.
- 2D tendon profile bands plus drafting profile views with station/elevation grid, high/low markers, and key point labels when the table contains `Station` and `Elevation` style columns.

## PTBot-Style Material Takeoff Direction

The PTBot benchmark suggests that tendon import should end in deliverables, not just geometry. In that direction, `DRAWING PT` now treats takeoff as part of the package:

- direct ADAPT import produces takeoff rows from grouped tendon/profile segments
- DWG/DXF import produces takeoff rows from detected CAD chains
- takeoff sheets are paged automatically when there are more items than fit on one drafting view
- the takeoff package is intended to become the base for later quantity, tagging, and checking workflows

## Accepted Column Names

The importer recognizes common variations of these names.

- Grouping: `Profile`, `Profile Name`, `Tendon`, `Tendon Name`, `Support Line`, `Span`
- Point order: `Point`, `Point No`, `Index`, `Sequence`
- 3D point geometry: `X`, `Y`, `Z`, `X Coordinate`, `Y Coordinate`, `Elevation`, `CGS`
- 2D profile geometry: `Station`, `Chainage`, `Distance`, `Elevation`, `Tendon CGS`, `Profile CGS`
- Segment geometry: `Start X`, `Start Y`, `Start Z`, `End X`, `End Y`, `End Z`
- Segment profile geometry: `Start Station`, `Start Elevation`, `End Station`, `End Elevation`

Units are read from column headers when possible, such as `(mm)`, `(m)`, `(ft)`, or `(in)`. If no unit is shown, large values are treated as millimeters and small values as meters.
