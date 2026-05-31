# PT Combined Extension Integration Plan

This note defines how `DRAWING PT` should combine existing extension capabilities already present in this codebase.

The goal is simple:

- do not build PT as an isolated add-on
- reuse the strongest existing extension pieces to accelerate delivery

## Core Idea

`DRAWING PT` should act as a workflow that orchestrates multiple internal extension layers:

1. `ADAPT import layer`
2. `CAD2MODEL / CAD source layer`
3. `shop drawing packaging layer`
4. `takeoff / quantity layer`
5. `linksheet / tabular review layer`
6. `future annotation automation layer`

## Existing Project Pieces We Should Reuse

### 1. DRAWING PT import entry

Current relevant code:

- `CamboBIMWindow.AdaptTendonImport.cs`
- `CadToModelRequest.cs`
- `CadToModelExternalEventHandler.cs`

This is the PT-specific front door.

It already supports:

- direct ADAPT import
- DWG/DXF import
- PT mark setup

## 2. CAD2MODEL behavior

Current relevant code:

- CAD source selection logic in the same `CadToModel...` pipeline

This matters because DWG/DXF fallback depends on:

- CAD source registration
- layer analysis
- selected CAD reference reuse

PT should keep using this instead of making a separate CAD-import subsystem.

## 3. Shop drawing packaging

Current relevant code:

- shared shop drawing helpers in `CadToModelExternalEventHandler.cs`

This already gives PT a strong base for:

- drafting views
- 3D review views
- viewports
- sheets
- sheet naming
- index sheets
- issue metadata

PT should continue to reuse this package layer rather than create PT-only sheet code everywhere.

## 4. Takeoff / quantity logic

Current relevant code:

- PT takeoff rendering now exists
- broader QS / BOQ systems also exist elsewhere in the repo

This means PT can mature in two steps:

1. keep using dedicated PT takeoff sheets now
2. later connect PT rows into more formal quantity/reporting flows

That is the right way to combine extensions without overcomplicating the first release.

## 5. Linksheet-style tabular review

Current relevant code:

- `Linksheet` request and preview flows

This is a very useful future integration point for PT because it can support:

- tendon review tables
- renumber editing
- grouped tendon audit
- material and metadata checking

This is one of the best internal extensions to combine with PT after Revit validation.

## 6. Future annotation automation

Current PT-specific outputs already exist:

- profile drafting views
- CAD chain-detail views
- mark bubbles
- baseline dimensions in direct profile views

Next PT automation should build on those views instead of inventing a new drawing environment.

That means future features should target:

- renumbering
- dimension generation
- bubble alignment
- text clash cleanup

inside the existing generated package views.

## Recommended Combined-Extension Build Order

### Stage 1. Keep PT import and package generation stable

Use:

- `DRAWING PT`
- `CAD2MODEL`
- shared `shop drawing packaging`

Goal:

- reliable direct ADAPT package
- reliable DWG/DXF package

## Stage 2. Add PT review and editing tools

Use:

- PT import data
- `Linksheet`-style tabular workflows

Goal:

- renumber manager
- tendon metadata review
- grouped tendon auditing

## Stage 3. Add PT quantity/reporting integration

Use:

- PT takeoff rows
- existing `QS / BOQ` reporting ideas where practical

Goal:

- stronger PT quantity deliverables
- better review consistency

## Stage 4. Add drafting automation

Use:

- generated PT drafting views
- shared sheet package output

Goal:

- dimensions
- bubble alignment
- text cleanup

## What We Should Not Do

We should not:

- create a second unrelated PT window
- create a separate CAD import subsystem just for PT
- create a separate sheet engine just for PT
- force PT into generic QS too early

The faster path is:

- reuse the working extension layers we already have
- keep PT-specific logic focused on tendon semantics and PT drafting behavior

## Immediate Next Recommendation

Right after live Revit validation, the best combined-extension feature to build is:

- `PT renumber and audit manager`

Why:

- it reuses current import data
- it fits naturally with linksheet-like review thinking
- it improves both direct ADAPT and DWG/DXF workflows
- it sets up dimensions, grouped drafting, and faster shop drawings

## Newly Implemented Backbone

The codebase now also includes two important integration foundations:

- `shared PT snapshot export`
- `global CAD shop-mark assignment across selected PT layers`

Why this matters:

- both user-facing methods can now produce one internal normalized PT data snapshot
- future Linksheet-style audit, renumber review, QS, and reporting tools can read one PT contract instead of two unrelated workflows
- DWG / DXF auto marks are now assigned once across the package instead of restarting per candidate layer

Current snapshot output location:

- `%APPDATA%\\MHNK\\RevitExtension\\DRAWING_PT\\Snapshots\\<date>`

Current snapshot artifacts:

- normalized PT `.json`
- PT takeoff-style `.csv`

Current renumber backbone:

- import-time sequence mode selection
- shared sequence logic for direct ADAPT and DWG / DXF paths
- optional CAD label preservation or forced renumbering for the DWG / DXF path
- ribbon entry split between `Import New PT` and `Renumber / Audit Existing PT`
- snapshot-backed PT audit preview before regeneration
- regeneration handoff that reuses the existing direct ADAPT and DWG / DXF import pipelines with updated mark settings
- persistent PT trace logging under `%APPDATA%\\MHNK\\RevitExtension\\DRAWING_PT\\Logs`
- PT-only Git save workflow for safer transfer to the Revit / ADAPT Builder PC
- dedicated Revit test checklist for direct ADAPT, DWG / DXF, and renumber / audit validation

## Drafting Automation Status

The current PT drafting pass now includes first-stage cleanup automation:

- CAD chain-detail dimension packages
- aligned bubble stacking for generated PT annotations
- first-pass text clash reduction on CAD preview chain labels
- first-pass text clash reduction on direct profile key-point labels
- stacked header / note block layout for CAD preview and chain-detail views
- cleaner multi-line metadata layout for direct profile views
- cleaner stacked side-table rows for CAD chain summaries and direct profile key-point tables
- anchored lower-axis labels for direct profile grids and end labels
- centered footer notes for CAD preview trimming and CAD chain-detail isolation notes
- top annotation rail for CAD preview tendon bubbles
- left/right annotation rails for direct profile key-point callouts
- width-bounded header / note wrapping for long PT source text and metadata blocks
- three-line side-rail profile callouts with stronger clearance from tendon markers and cleaner vertical staggering

Why this matters:

- generated PT sheets are moving closer to a reviewed shop-drawing look
- both import options now share the same cleanup direction instead of diverging
- tomorrow's live Revit test can focus on readability, not only raw import success
