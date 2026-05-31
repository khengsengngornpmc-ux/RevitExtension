# PT Revit Test Checklist

## Goal

Use this checklist on the Revit / ADAPT Builder PC to validate the current `DRAWING PT` workflow before deeper polish.

## Test Inputs

- One direct ADAPT source:
  - `.adm` preferred
  - tendon/profile table fallback if needed
- One ADAPT-exported CAD source:
  - `.dwg` or `.dxf`
- One Revit model with:
  - a usable title block
  - plan or drafting view open before running `DRAWING PT`

## Direct ADAPT Test

1. Open Revit and load the extension.
2. Start `DRAWING PT`.
3. Choose `Import New PT`.
4. Choose `Direct ADAPT Import`.
5. Select the `.adm` or tendon/profile source.
6. Confirm PT mark settings.
7. Verify:
   - no import failure dialog
   - status bar reports segment/profile counts
   - tendon solids and centerlines appear for 3D-capable sources
   - drafting profile views are created
   - profile sheets are created when a title block exists
   - takeoff sheet is created
   - 3D review sheet is created for model-based imports

## DWG / DXF Test

1. Start `DRAWING PT`.
2. Choose `Import New PT`.
3. Choose `DWG / DXF Import`.
4. Select the ADAPT-exported `.dwg` or `.dxf`.
5. Confirm link/import mode.
6. Confirm PT mark settings.
7. Verify:
   - CAD file is linked or imported successfully
   - PT layers are detected
   - chain-detail drafting views are created
   - PT package sheets are created
   - takeoff sheet is created
   - CAD PT 3D review sheet is created if applicable

## Profile Graphics Check

Review at least one direct profile drafting view and confirm:

- tendon band is visible
- header text wraps cleanly
- metadata blocks do not collide
- `Start / End / High / Low` callouts stay on side rails
- callout text does not sit on top of the tendon band
- station and elevation labels remain readable
- shop-mark bubbles are readable

## CAD Drafting Check

Review at least one CAD PT preview and one CAD chain-detail sheet and confirm:

- header blocks wrap instead of overrunning
- chain summary table stays readable
- footer notes are centered and readable
- chain marks and sequence numbers look correct

## Renumber / Audit Test

1. Start `DRAWING PT`.
2. Choose `Renumber / Audit Existing PT`.
3. Reuse the latest snapshot if available.
4. Change prefix, start number, digits, or sequence mode.
5. Review the preview.
6. Confirm regeneration.
7. Verify:
   - preview opens
   - proposed marks match expectation
   - regenerated PT package uses the new marks
   - CAD label preservation works when enabled

## Logs To Review

Check these folders after each test:

- `%APPDATA%\\MHNK\\RevitExtension\\DRAWING_PT\\Snapshots`
- `%APPDATA%\\MHNK\\RevitExtension\\DRAWING_PT\\Reports`
- `%APPDATA%\\MHNK\\RevitExtension\\DRAWING_PT\\Logs`

## Record Failures

For any failure, capture:

- source file name
- import option used
- mark settings used
- active Revit view type
- last status-bar message
- relevant log file entry
- screenshot of the bad sheet or view
