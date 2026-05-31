# ADAPT Profile Visual Roadmap

This note focuses on the visual side of direct ADAPT tendon import into Revit.

The current code already creates:

- 3D tendon solids
- 3D tendon centerlines
- active-view 2D tendon profile elements
- drafting profile views with grid, markers, and summary annotations
- dedicated ADAPT profile sheets when a title block is available
- ADAPT PT takeoff sheets derived from grouped tendon metadata

The next goal is to make the Revit result feel closer to ADAPT-Builder or Visicon profile output.

## Current Rendering Stack

Main rendering methods:

- `ImportAdaptTendonProfiles(...)`
- `DrawAdaptProfileDetailGroup(...)`
- `DrawAdaptProfileDraftingView(...)`
- `DrawAdaptProfileBand(...)`
- `BuildAdaptOrderedPathPoints(...)`

Current tuning settings are centralized in:

- `GetDefaultAdaptProfileRenderSettings(...)`

This is now the best place to adjust profile presentation without changing the full pipeline.

## What "Closer To ADAPT / Visicon" Means

The Revit output should aim for these qualities:

- one clean tendon profile per tendon/group
- stable path continuity even when source segments are messy
- readable station/elevation grid
- clear start, end, high point, and low point labels
- tendon body that reads visually as a tendon profile, not a thin line
- balanced whitespace and title layout
- consistent presentation across short and long tendons

## Best Next Visual Improvements

### 1. Better Graphic Hierarchy

Improve contrast between:

- tendon outline
- tendon centerline
- grid lines
- axis lines
- key point markers
- title and summary text

Practical code targets:

- `EnsureAdaptTendonLineStyle(...)`
- `DrawAdaptProfileBand(...)`
- `DrawAdaptProfileGrid(...)`

## 2. More ADAPT-Like Annotation

Add richer labels such as:

- end elevations
- drape value
- low-point station
- high-point station
- support locations if source metadata becomes available

Best code target:

- `DrawAdaptProfileDraftingView(...)`

## 3. Scale And Layout Intelligence

Different tendon sizes should not all use the same visual spacing.

Improve:

- title block position
- summary column position
- marker label collision handling
- grid density
- minimum visible tendon thickness

Best code target:

- `GetDefaultAdaptProfileRenderSettings(...)`

## 4. Sheet-Ready Profile Package

The code now packages each generated profile drafting view into a dedicated sheet when a title block is available. The next improvements in this area are:

- named profile view
- consistent viewport placement
- optional index or summary sheet
- shop-drawing-ready annotation workflow

Best code targets:

- `SyncAdaptProfileDraftingViews(...)`
- `SyncAdaptProfileSheets(...)`

## 5. Richer Source Metadata

If more data can be extracted from `.adm`, use it to improve the profile:

- tendon size
- duct size
- support points
- tendon type
- strand count
- stress/drape naming

Best code target:

- `ReadAdaptAdmTendonGeometry(...)`

## Recommended Development Order

1. Tune render settings against real Revit screenshots.
2. Improve visual hierarchy for line styles and labels.
3. Add richer point and elevation annotations.
4. Refine the new sheet-ready profile packaging and add optional index packaging.
5. Expand `.adm` metadata extraction.

## Important Constraint

This PC does not currently have Revit for runtime verification.

Because of that, the best code-side strategy is:

- centralize rendering settings
- keep drawing methods modular
- make layout changes in one place
- avoid hard-coding more visual values than necessary

That preparation work is already underway in the current implementation.
