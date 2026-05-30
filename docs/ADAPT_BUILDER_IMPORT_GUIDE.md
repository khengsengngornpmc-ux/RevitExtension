# ADAPT-Builder To Revit Import

This add-in does not parse ADAPT `.adm` project data directly. ADAPT-Builder 2018/2019 stores project data in proprietary/native files and native DLLs, so the supported bridge is to export or copy data from ADAPT, then import that exported file into Revit.

If an `.adm` file is selected in `Import ADAPT`, the add-in first looks for a nearby DWG/DXF export from that ADAPT project. If one is found, it offers to import it immediately. If no export is ready, it offers to open the model in ADAPT-Builder and then guides the user back to the DWG/DXF or table import path.

## Verified Local Install

- ADAPT-Builder 2018: `C:\Program Files (x86)\ADAPT\ADAPT-Builder 2018\builder.exe`
- ADAPT-Builder 2019 is also installed on this PC.
- `.adm` files are associated with ADAPT-Builder 2018 on this machine.

## Recommended Workflows

### Tendon Plan Geometry

1. In Revit, open `MHNK > STR > CAD2MODEL`.
2. Click `Import ADAPT`.
3. Select the ADAPT `.adm` model if it is not already open.
4. If the add-in finds a nearby DWG/DXF export, confirm the prompt to import it.
5. If no export is ready, confirm the prompt to open the model in ADAPT-Builder.
6. In ADAPT-Builder, turn on the tendon display and hide other items as needed.
7. Export the tendon plan using ADAPT's DWG/DXF export.
8. Return to Revit.
9. Click `Import ADAPT` again and select the DWG/DXF. The dialog starts in the last ADAPT folder used during this Revit session.
10. The add-in links the drawing at the project origin, selects it as the CAD2MODEL source, and loads the CAD layers.
11. Use existing CAD2MODEL tools to pick CAD geometry or layers.

If the `.adm` is already open in ADAPT-Builder, start at the export step:

1. Export the tendon plan using ADAPT's DWG/DXF export.
2. In Revit, open `MHNK > STR > CAD2MODEL`.
3. Click `Import ADAPT` and select the DWG/DXF.
4. The add-in links the drawing at the project origin, selects it as the CAD2MODEL source, and loads the CAD layers.
5. Use existing CAD2MODEL tools to pick CAD geometry or layers.

### Tendon/Profile Table

1. Export or copy a tendon/profile point table from ADAPT/report output to CSV, TXT, TSV, XLSX, XLSM, or XLS.
2. In Revit, open `MHNK > STR > CAD2MODEL`.
3. Click `Import ADAPT`.
4. Select the exported table.

The importer creates:

- A selected CAD2MODEL source when the file is ADAPT-exported DWG/DXF.
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
