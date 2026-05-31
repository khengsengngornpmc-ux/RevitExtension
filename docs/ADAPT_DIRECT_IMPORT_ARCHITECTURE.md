# ADAPT Direct Import Architecture

This note describes how `DRAWING PT` imports ADAPT tendon/profile data into Revit without launching ADAPT-Builder.

## Entry Point

User command:

- `MHNK > STR > DRAWING PT`
- Revit command class: `OpenDrawingPtCommand`

Main UI/import workflow:

- `CamboBIMWindow.AdaptTendonImport.cs`

## End-To-End Method Flow

1. User selects an ADAPT source file in the `DRAWING PT` import picker.
2. The UI decides which import path to use:
   - `.adm` project: direct tendon parser
   - `.dwg` / `.dxf`: CAD link/import path
   - `.csv` / `.txt` / `.tsv` / `.xlsx` / `.xlsm` / `.xls`: table parser
3. The selected source is converted into `AdaptTendonProfileSegmentPayload` items.
4. Those payload segments are sent through the external event request.
5. Revit-side code creates model solids, centerlines, active-view profile elements, drafting profile views, and profile sheets when a title block is available.
6. For 3D tendon imports, the Revit-side code also creates a dedicated PT model review sheet with an isometric 3D view.
7. The same Revit-side import also builds PT takeoff rows from the grouped tendon data and creates takeoff drafting sheets when a title block is available.

## Key Methods

### UI And Dispatch

- `QueueAdaptTendonProfileImport(...)`
  - Packs parsed tendon segments into the request object
  - Sets the import mode
  - Raises the Revit external event

### Source Parsing

- `ReadAdaptTendonProfileFile(...)`
  - Chooses parser based on file extension

- `ReadAdaptAdmTendonGeometry(...)`
  - Direct `.adm` parser
  - Reads tendon/profile point records from the ADAPT project file
  - Converts ADAPT coordinates into Revit feet
  - Produces `AdaptTendonProfileSegmentPayload` segments

- `ReadAdaptTendonProfileRows(...)`
  - Table-based parser
  - Detects whether the source is 3D geometry or station/elevation profile data

- `AppendAdaptTendonSegments(...)`
  - Builds segments from explicit start/end columns

- `AppendAdaptTendonPointSegments(...)`
  - Builds segments by connecting ordered points within each tendon/profile group

## Revit Creation

- `ImportAdaptTendonProfiles(...)`
  - Main Revit-side creation entry point
  - Deletes prior imported ADAPT geometry from the same source
  - Branches by import mode

### 3D Model Path

- `BuildAdaptTendonSolids(...)`
  - Creates tendon solids for grouped segments

- `CreateAdaptTendonSegmentSolid(...)`
  - Extrudes a circular profile along each tendon segment

- `CreateAdaptModelLine(...)`
  - Creates centerline geometry for each segment

### 2D Profile Path

- `DrawAdaptProfileDetailGroup(...)`
  - Creates visible tendon-profile elements in the current 2D view

- `SyncAdaptProfileDraftingViews(...)`
  - Creates or updates dedicated drafting views per tendon/profile group

- `SyncAdaptProfileSheets(...)`
  - Rebuilds a dedicated ADAPT profile sheet set from the drafting views
  - Reuses the existing sheet placement and title block metadata helpers

- `SyncAdaptProfileModelReviewSheet(...)`
  - Builds a 3D review sheet from the created tendon solids and centerlines
  - Uses the shared shop-drawing model-view helper so the tendon package includes an inspection view
  - This aligns with the PTBot `Create 3D View` concept, where 3D review is used to inspect segment elevations and catch issues hidden in 2D

- `SyncAdaptProfileTakeoffSheet(...)`
  - Builds PT takeoff rows from grouped direct-import tendon segments
  - Creates one or more takeoff drafting views and sheets
  - Pages automatically when the takeoff exceeds one sheet

- `DrawAdaptProfileDraftingView(...)`
  - Draws the profile package in a drafting view
  - Adds the tendon band, grid, key point markers, and annotations

- `DrawAdaptPtTakeoffView(...)`
  - Draws the PT takeoff drafting view header and summary

- `DrawAdaptPtTakeoffTable(...)`
  - Draws the takeoff table body used by both direct ADAPT and DWG/DXF workflows

## Current Visual Strategy

The current Revit profile result is built from grouped tendon segments:

- order the segments into a continuous path
- compute profile stations from the path
- draw a tendon band around the centerline
- annotate station/elevation information in drafting views
- package each generated drafting profile view onto a dedicated ADAPT profile sheet when possible
- build PT takeoff rows and package them onto dedicated takeoff sheets when possible

This is intended to move the result closer to ADAPT/Visicon-style profile review output instead of just showing raw lines.

## Best Next Improvement Targets

If the goal is to get even closer to ADAPT-style tendon presentation, the best next methods to improve are:

- `DrawAdaptProfileDraftingView(...)`
  - best place to improve labels, scales, grid appearance, and presentation layout

- `DrawAdaptProfileBand(...)`
  - best place to improve the tendon body appearance

- `BuildAdaptOrderedPathPoints(...)`
  - best place to improve tendon continuity when source geometry is messy

- `ReadAdaptAdmTendonGeometry(...)`
  - best place to enrich source metadata if `.adm` exposes more tendon properties later

For visual tuning priorities, see:

- `docs/ADAPT_PROFILE_VISUAL_ROADMAP.md`

For the higher-level two-path product design, see:

- `docs/ADAPT_TWO_OPTION_IMPORT_STRATEGY.md`
