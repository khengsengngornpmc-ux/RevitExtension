# ADAPT-Builder To Revit Import

`DRAWING PT` can import ADAPT tendon geometry directly from supported `.adm` files without opening ADAPT-Builder. The direct `.adm` parser reads tendon profile/control-point records from the project file in read-only mode and creates Revit tendon profile curves.

Use `MHNK > STR > DRAWING PT` for the dedicated ADAPT/PT workflow. The button opens the ADAPT import picker directly.

If an `.adm` file is selected, the add-in first tries direct tendon profile import from the `.adm`. If direct profile geometry is not found, it looks for a nearby DWG/DXF export from that ADAPT project and offers to import it. If neither path is available, it stops and asks the user to select an ADAPT-exported DWG/DXF or tendon/profile table. `DRAWING PT` does not launch ADAPT-Builder.

The tool remembers the last ADAPT folder and project between Revit sessions. On the next `DRAWING PT` click, it can offer the latest DWG/DXF export from the previous ADAPT project before asking the user to browse again.

For DWG/DXF files, `DRAWING PT` asks whether to link the export or import it into the model. Link is recommended because the source remains external and easier to refresh/manage; import is available when an embedded CAD copy is required. The last selected mode is remembered between Revit sessions.

If the same ADAPT CAD export is already in the Revit model, `DRAWING PT` asks whether to reuse the existing CAD source or import another copy. Each CAD import/reuse attempt is logged under `%APPDATA%\MHNK\RevitExtension\DRAWING_PT\Reports`.

After a DWG/DXF import or reuse, the tool analyzes CAD layer names and reports likely PT/tendon/profile layers in the status message and audit log. If no PT-specific layer name is obvious, it reports the top populated CAD layers so the user can inspect the export quickly.

## Verified Local Install

- ADAPT-Builder 2018 exists at `C:\Program Files (x86)\ADAPT\ADAPT-Builder 2018\builder.exe`, but `DRAWING PT` does not use or open this executable for `.adm` import.
- ADAPT-Builder 2019 is also installed on this PC.
- `.adm` files are associated with ADAPT-Builder 2018 on this machine, but the Revit add-in reads the selected file path directly.

The sample file `C:\Users\PC\Desktop\New folder\6950022_1F-12STRANDS_round_duct.adm` was inspected as a binary ADAPT project file. Its direct tendon profile/control-point records were readable without starting ADAPT-Builder.

## Recommended Workflows

### Direct `.adm` Tendon Geometry

1. In Revit, open `MHNK > STR > DRAWING PT`.
2. Select the ADAPT `.adm` model.
3. If tendon profile records are found, the add-in creates `MHNK ADAPT Tendon` profile curves in Revit.
4. Check the status bar for the number of imported profiles, points, segments, and inferred units.

The direct parser uses ADAPT profile/control-point blocks rather than the flat plan centerline. ADAPT X/Z are treated as plan/station axes, and ADAPT Y is treated as the Revit elevation axis.

### DWG/DXF Fallback

Use this when direct `.adm` import does not find readable tendon geometry or when you prefer the ADAPT drawing appearance.

1. Export the tendon plan using ADAPT's DWG/DXF export.
2. In Revit, click `MHNK > STR > DRAWING PT` and select the DWG/DXF.
3. The add-in links the drawing at the project origin, selects it as the CAD2MODEL source, and loads the CAD layers.
4. Use existing CAD2MODEL tools to pick CAD geometry or layers.

### Tendon/Profile Table

1. Export or copy a tendon/profile point table from ADAPT/report output to CSV, TXT, TSV, XLSX, XLSM, or XLS.
2. In Revit, click `MHNK > STR > DRAWING PT`.
3. Select the exported table.

The importer creates:

- A selected CAD2MODEL source when the file is ADAPT-exported DWG/DXF.
- CAD instance comments with ADAPT PT source metadata after a DWG/DXF link/import.
- Duplicate detection for previously linked/imported ADAPT PT CAD exports.
- PT/tendon/profile layer candidates from the imported DWG/DXF.
- 3D model lines when the table contains `X`, `Y`, and `Z` style columns.
- 2D profile detail lines when the table contains `Station` and `Elevation` style columns.

## Accepted Column Names

The importer recognizes common variations of these names.

- Grouping: `Profile`, `Profile Name`, `Tendon`, `Tendon Name`, `Support Line`, `Span`
- Point order: `Point`, `Point No`, `Index`, `Sequence`
- 3D point geometry: `X`, `Y`, `Z`, `X Coordinate`, `Y Coordinate`, `Elevation`, `CGS`
- 2D profile geometry: `Station`, `Chainage`, `Distance`, `Elevation`, `Tendon CGS`, `Profile CGS`
- Segment geometry: `Start X`, `Start Y`, `Start Z`, `End X`, `End Y`, `End Z`
- Segment profile geometry: `Start Station`, `Start Elevation`, `End Station`, `End Elevation`

Units are read from column headers when possible, such as `(mm)`, `(m)`, `(ft)`, or `(in)`. If no unit is shown, large values are treated as millimeters and small values as meters.
