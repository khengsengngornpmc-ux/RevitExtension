# ADAPT Two-Option Import Strategy

This document defines the recommended product design for importing post-tensioning data into Revit from ADAPT workflows.

The recommended design has two explicit user-facing options:

1. `Direct ADAPT Import`
2. `DWG / DXF Import`

Important constraint:

- this design does `not` require ADAPT-Builder to export JSON
- if JSON is used, it should be only as an internal add-in data format after import parsing

This aligns with the workflow pattern shown by Visicon, where ADAPT tendon data is first interpreted into realistic 3D tendon geometry and then brought into Revit for coordination and documentation.

Reference material:

- Visicon tutorial page: `https://visicon.com/video-tutorial-how-to-create-3d-tendons-in-revit/`
- Visicon overview: `https://visicon.com/overview/`
- Visicon getting started guide: `https://visicon.com/downloads/Visicon2.1GettingStartedGuide.pdf`
- YouTube video: `https://www.youtube.com/watch?v=So1Xp5DGiK8`
- YouTube video: `https://www.youtube.com/watch?v=8Re2K_JzvXk`
- YouTube video: `https://www.youtube.com/watch?v=039-YOG4liw`

## Product Goal

`DRAWING PT` should support two different import intents:

- `Option 1: Design-intent import`
  - read tendon data directly from ADAPT-originated source data
  - create structured, editable Revit tendon representation
  - best for BIM coordination and model-based review

- `Option 2: Drawing-appearance import`
  - bring in ADAPT-produced CAD drawings
  - preserve the original exported drafting appearance
  - best for visual tracing, drawing background, and appearance matching

The two options should coexist, not compete.

## Why Two Options Are Necessary

Direct ADAPT data and DWG/DXF files serve different purposes.

### Direct ADAPT Import

Best for:

- 3D coordination
- clash review
- realistic tendon geometry
- metadata-driven grouping
- future automation
- consistent drafting/profile generation inside Revit

Weakness:

- harder to match ADAPT's exact drafting appearance immediately

### DWG / DXF Import

Best for:

- preserving ADAPT-exported linework
- user confidence when they want to see the same graphics they know from ADAPT
- quick background/reference import
- cases where direct geometry parsing misses something

Weakness:

- imported CAD is not true tendon geometry
- limited metadata
- weaker downstream automation

## Recommended UX

When the user clicks `MHNK > STR > DRAWING PT`, the tool should clearly offer two paths.

### Option 1: Direct ADAPT Import

User-facing wording:

- `Import ADAPT Model Data`
- subtitle: `Create Revit tendon geometry and profile views from ADAPT source data`

Accepted sources:

- `.adm` directly, if usable
- ADAPT table exports: `.csv`, `.txt`, `.tsv`, `.xlsx`, `.xlsm`, `.xls`
- future ideal source if available: `.inp`-style exchange data

Output:

- 3D tendon solids
- 3D tendon centerlines
- 2D tendon profile elements
- drafting profile views
- dedicated ADAPT profile sheets
- ADAPT profile index sheet
- ADAPT PT model review sheet for 3D inspection
- ADAPT PT takeoff sheets with paging when required
- stable PT shop marks shared across package outputs
- import-time PT mark prefix, start number, and digit control
- grouped metadata per tendon/profile

### Option 2: DWG / DXF Import

User-facing wording:

- `Import ADAPT CAD Drawing`
- subtitle: `Link or import ADAPT-exported DWG/DXF for drawing-based reference`

Accepted sources:

- `.dwg`
- `.dxf`

Output:

- linked or imported CAD
- CAD2MODEL source selection
- layer analysis for PT/tendon/profile layers
- native drafting preview views from top PT candidate layers
- connected PT chain detection with CAD-based or generated marks
- deterministic auto marks when CAD labels are missing
- import-time PT mark prefix, start number, and digit control
- isolated chain-detail views for strongest detected PT paths
- CAD PT model review sheet for 3D package inspection
- DWG/DXF PT package sheets and index sheet when a title block is available
- DWG/DXF PT takeoff sheets built from detected tendon chains
- optional downstream tracing / conversion workflows

## Design Principle

Option 1 is the `primary smart workflow`.

Option 2 is the `appearance-preserving fallback workflow`.

That should also shape the UI:

- show Option 1 first
- mark Option 1 as recommended
- describe Option 2 as reference-based fallback or drawing import

## Architecture Overview

### Option 1: Direct ADAPT Import Pipeline

Current project path already supports most of this pipeline:

1. source selection in `DRAWING PT`
2. parse `.adm` or tendon/profile tables
3. convert into `AdaptTendonProfileSegmentPayload`
4. send payload through external event
5. create Revit geometry, profile views, and profile package sheets

Current key methods:

- `ReadAdaptAdmTendonGeometry(...)`
- `ReadAdaptTendonProfileRows(...)`
- `QueueAdaptTendonProfileImport(...)`
- `ImportAdaptTendonProfiles(...)`

The PTBot RAM Concept import-model reference reinforces that this path should be treated as:

- structured model-data import
- smart tendon creation
- review and package generation inside Revit

For the deeper step-by-step interpretation, see:

- `docs/PTBOT_RAM_CONCEPT_IMPORT_WORKFLOW.md`

### Option 2: DWG / DXF Import Pipeline

Current project path already supports this as well:

1. user selects DWG/DXF
2. choose link or import mode
3. bring CAD into Revit
4. register as CAD2MODEL source
5. analyze likely PT layers
6. generate native PT preview drafting views and package sheets from top candidate layers

Current key methods:

- `QueueAdaptCadDrawingImport(...)`
- Revit-side CAD import handler

## What The Visicon Workflow Suggests

The Visicon references consistently point to a strong pattern:

- import ADAPT tendon data directly
- create realistic 3D tendon geometry
- support tendon layout adjustment and review
- bring final geometry into Revit for coordination and documentation

The YouTube tutorial `3D Tendons Modelling in Revit by Help of Visicon` reinforces the same direction.

From the video metadata and description:

- the workflow example uses Visicon to transfer tendons modeled in `ADAPT-Builder`
- the tendons are created in `Revit`
- the target output is `realistic 3D BIM tendons`

Design implication:

- the direct ADAPT path should be treated as the premium BIM workflow
- the DWG / DXF path should be treated as the drawing-reference workflow
- the direct path should optimize for true tendon objects, not just copied linework

Another useful reference is the YouTube video `Slab tendons in a BIM-Workflow Revit + shop drawings`.

From the video metadata and description:

- tendon information is generated directly in `Revit`
- the workflow includes `3D tendon geometry`
- the workflow extends into `shop drawings`
- the workflow also includes `consistency checks`, `alignment`, `dimensioning`, `clash checks`, and `material take-off`

Design implication:

- our target should not stop at import
- the direct ADAPT path should become a BIM workflow foundation
- generated profile views should evolve toward documentation-ready output
- generated profile packages should include sheet indexing and package navigation
- generated PT packages should include material-takeoff output, not only geometry views
- generated PT packages should include 3D review output, not only 2D drafting sheets
- future development should consider validation, clash-review, and quantity workflows after tendon creation

The RISA workflow reference `Designing Post-Tensioning using Autodesk Revit, ADAPT-Builder and ADAPT-PT/RC` adds one more important interpretation:

- the direct ADAPT path should be treated as the `return-to-Revit PT design result` workflow
- Revit is the coordination and documentation environment after ADAPT design is completed

For the deeper interpretation, see:

- `docs/RISA_ADAPT_REVIT_RETURN_WORKFLOW.md`

## Combined Extension Direction

This project should also reuse multiple internal extension layers rather than building PT as an isolated feature.

Best-fit combined layers are:

- `DRAWING PT` import flow
- `CAD2MODEL` source handling
- shared `shop drawing` helpers
- PT takeoff views now, with possible future `Linksheet` and `QS / BOQ` integration later

For the integration roadmap, see:

- `docs/PT_COMBINED_EXTENSION_INTEGRATION_PLAN.md`

That suggests our Option 1 should be treated as the long-term flagship workflow, not just a parser.

In practical terms, that means our direct import should eventually support:

- tendon type awareness
- strand / duct size awareness
- better tendon body graphics
- anchor and end condition representation
- profile packages suitable for sheets
- package index sheets for faster navigation and issue control
- better visual similarity to ADAPT/Visicon output

## Recommended Functional Split

### Option 1: Direct ADAPT Import Should Own

- tendon geometry
- tendon metadata
- tendon grouping
- future linked-tendon grouping for similar tendon families
- future chair/support awareness for PT checking and detailing
- future chair-detail display modes for different PT package outputs
- 3D representation
- generated profile views
- sheet-ready tendon documentation
- ADAPT package index sheets
- PT takeoff rows and sheet-ready takeoff output
- future PT dimension automation in generated package views
- future bubble-alignment cleanup for generated PT marks and labels
- future clash / host coordination intelligence

### Option 2: DWG / DXF Import Should Own

- imported drawing reference
- exact exported CAD appearance
- fallback when direct parsing is incomplete
- user trust during transition period
- native PT candidate preview packages for tracing and review
- preliminary tendon-chain grouping and mark logic from CAD geometry
- lighter similarity-linking for repeated CAD-derived chains
- chain-level review sheets from CAD-derived tendon paths
- chain-based PT takeoff output when direct source data is unavailable
- early PT dimension automation in CAD-derived chain-detail views
- early bubble-alignment cleanup in CAD-derived package views

## Output Expectations Per Option

### Option 1 Output Standard

Minimum:

- one tendon group becomes one clean Revit tendon result
- visible 3D solids
- visible centerlines
- drafting profile view per tendon/profile group
- profile band, grid, key markers, summary text
- dedicated profile sheets when a title block is available
- PT takeoff sheets built from the grouped tendon metadata

Target:

- visual quality close to ADAPT / Visicon
- predictable naming
- clean reimport / replace behavior
- profile package index sheet
- stronger shop-drawing workflow

### Option 2 Output Standard

Minimum:

- CAD imported or linked at the correct location
- source remembered
- duplicate detection
- usable CAD layer discovery
- native drafting preview views from top PT candidate layers
- connected chain summary and PT-style mark labeling inside the preview package
- isolated chain-detail sheets for stronger tendon-by-tendon review

Target:

- one-click convert selected CAD tendon geometry into stronger native Revit helper geometry
- packaged CAD PT sheet sets for review and tracing
- optional background-to-native workflow

## Recommended UI Layout

Suggested import dialog sections:

### Section A: Import Mode

- `Direct ADAPT Import (Recommended)`
- `DWG / DXF Import`

### Section B: Source File

- browse file
- show recognized type
- show recent project path if available

### Section C: Output Preview

For direct import:

- detected profiles
- detected points
- inferred units
- inferred mode: `3D` or `Profile`

For CAD import:

- import mode: `Link` or `Import`
- candidate PT layers if already known

## Implementation Priority

### Phase 1

Stabilize both modes and make the distinction explicit in UI and docs.

### Phase 2

Make Option 1 visually much better than raw lines.

### Phase 3

Allow Option 2 CAD data to assist Option 1 when direct data is incomplete.

Example hybrid cases:

- use direct ADAPT data for geometry
- use DWG for visual comparison
- use CAD layers to verify tendon grouping or naming

## Recommended Development Decision

The project should officially treat:

- `Direct ADAPT Import` as the main product direction
- `DWG / DXF Import` as the compatibility and appearance-preserving path

That is the cleanest interpretation of both:

- the Visicon-style workflow
- the current codebase
- the user expectation of seeing tendon profiles in Revit, not just lines

## Current Codebase Status

The current implementation already leans in this direction:

- direct `.adm` and table parsing exist
- direct import creates solids and generated profile graphics
- DWG/DXF fallback already exists
- CAD import mode already remembers link/import preference
- DWG/DXF import now creates PT candidate preview views and package sheets when possible

What remains is mostly product refinement:

- clearer two-option UX
- more ADAPT-like visual output for Option 1
- stronger integration between direct-data and CAD-reference workflows
- richer CAD-to-native tendon conversion beyond preview packaging
