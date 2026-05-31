# PTBots Channel Alignment Matrix

This note translates the `mindCreations` / `PTBot` channel themes into a practical build matrix for `DRAWING PT`.

The intent is not to copy PTBot blindly.

The intent is to keep our product pointed at the same class of outcome:

- import tendon data into Revit
- create smart tendon representations
- automate PT drafting tasks
- support checking and review
- produce shop-drawing and takeoff deliverables

Important project rule:

- our product still stays on `2 user-facing import options only`
- `Option 1`: direct import from `ADAPT-Builder` source data
- `Option 2`: import from `DWG / DXF`

## Exact Video Inventory Reviewed

The following `mindCreations / PTBot Revit` videos were reviewed again for this matrix.

All of these referenced videos were published on `September 22, 2020`.

- `PTBot Revit - Link Tendons`
  - `https://www.youtube.com/watch?v=FtDB4femkZs`
- `PTBot Revit - Align Tendon Bubbles`
  - `https://www.youtube.com/watch?v=UHUswSVqNb8`
- `PTBot Revit - Dimension Tendons`
  - `https://www.youtube.com/watch?v=KMso7ZJSDow`
- `PTBot Revit - Renumber Tendons`
  - `https://www.youtube.com/watch?v=bjpPRmpPtWg`
- `PTBot Revit - Show/Hide Intermediate Chairs`
  - `https://www.youtube.com/watch?v=PXbH-jHN1vk`
- `PTBot Revit - Resolve Chair Clashes`
  - `https://www.youtube.com/watch?v=DeOb76jq-jA`
- `PTBot Revit - Resolve Tendon Clashes`
  - `https://www.youtube.com/watch?v=Nn7kSGI4xQM`
- `PTBot Revit - Create 3D View`
  - `https://www.youtube.com/watch?v=VErnlrXrlpw`
- `PTBot Revit - Material Takeoff`
  - `https://www.youtube.com/watch?v=ttUfDz5qTJ8`

## Tool-By-Tool Workflow Read

This section is based on the public video descriptions plus the auto-captions extracted from those video pages.

### 1. Link Tendons

Observed workflow:

- user runs a `Link` command
- PTBot prompts for search criteria
- search criteria include:
  - distance
  - angle
  - percentage similarity
- the result creates a `master tendon` display and reduces similar tendons to lighter linked members
- slave tendons lose duplicate chairs and labels
- link segments remain so the grouped tendons are still traceable

Best translation for our project:

- direct ADAPT should eventually group similar tendons by geometry plus tendon metadata
- DWG / DXF can only approximate this through chain similarity unless richer CAD data exists

### 2. Align Tendon Bubbles

Observed workflow:

- user selects tendons
- user runs `Align Bubble`
- user selects a reference line, in the demo a grid line
- PTBot aligns bubbles for tendons perpendicular to that reference
- the tool is explicitly reference-based, not random auto-spacing

Best translation for our project:

- generated bubbles should support alignment to a guide line, grid, or calculated sheet baseline
- our current bubble stacking is a good first stage, but PTBot is still stronger because it aligns to a chosen drafting reference

### 3. Dimension Tendons

Observed workflow:

- user selects tendons
- user runs `Add Dimension`
- PTBot prompts for a dimension type
- user chooses the location of the dimension line
- user can include additional slab edges in the dimension run

Best translation for our project:

- PT dimensioning should not be only automatic annotations
- we should support both generated default dimensions and later user-guided dimension references

### 4. Renumber Tendons

Observed workflow:

- user can set a start number
- renumber supports `by selection`
- renumber also supports `by line`
- `by line` uses a picked start point and end point
- tendons intersected by that line are renumbered by distance from the line start

Best translation for our project:

- our current sequence modes are strong groundwork
- the missing PTBot-equivalent layer is a dedicated post-import renumber command with line-driven ordering

### 5. Show / Hide Intermediate Chairs

Observed workflow:

- after tendons are linked, PTBot can reduce drawing clutter
- in rendering settings the user can toggle `intermediate drapes`
- when enabled, labels and additional text are generated for intermediate chairs between high and low points

Best translation for our project:

- chair display is a presentation mode, not only raw geometry
- direct ADAPT needs trustworthy chair/support metadata before this can be implemented well

### 6. Resolve Chair Clashes

Observed workflow:

- user selects text notes
- PTBot filters the selection to text notes
- the tool checks each label against view geometry and other labels
- if clashes are found it tries to move the label to a clear space
- unresolved cases are flagged with a red rectangle for manual review

Best translation for our project:

- clash cleanup must be rerunnable after annotation generation
- unresolved labels should be visibly marked, not silently left in bad positions

### 7. Resolve Tendon Clashes

Observed workflow:

- user selects tendons
- PTBot warns that the tool may take time on large selections
- the tool performs volumetric duct comparison by segment
- clashing tendons are listed
- clicking warnings highlights the clashing segments
- the user can then adjust chair heights manually

Best translation for our project:

- tendon clash review should be segment-based and volume-based, not line-intersection only
- direct ADAPT is the right path for this because it has the best chance of producing real tendon solids

### 8. Create 3D View

Observed workflow:

- user runs `Create 3D`
- PTBot creates a volume for each selected tendon segment
- ducts are placed at the correct elevations using high and low points
- the tool reports errors after generation
- the 3D view is used for inspection, anomaly review, and clash spotting

Best translation for our project:

- our 3D review should be treated as a QA tool, not only a visual extra
- error reporting on impossible geometry is part of the workflow, not optional polish

### 9. Material Takeoff

Observed workflow:

- user runs `Material Takeoff`
- PTBot exports a tabular file
- output includes anchorage counts and types
- output includes strand tonnage
- output includes chair counts
- another tab includes tendon-level detail such as length and quantity information

Best translation for our project:

- takeoff needs both summary and detailed tendon tabs
- current CSV snapshot export is useful, but PTBot is still ahead on deliverable polish

## Channel-Derived Capability Matrix

| PTBot-style capability | Channel / official theme | Direct ADAPT status | DWG / DXF status | Next target in our code |
| --- | --- | --- | --- | --- |
| Import tendons | official PTBot feature | strong in-progress with real `.adm` parsing path | strong in-progress with CAD package path | keep strengthening `ReadAdaptAdmTendonGeometry(...)`, `ReadAdaptTendonProfileRows(...)`, and CAD package analysis |
| Render smart tendon objects | official PTBot feature | partial with generated profile views and 3D tendon solids | limited because chains are inferred from CAD graphics | keep improving tendon metadata, grouping, and object semantics in `ImportAdaptTendonProfiles(...)` |
| Link similar tendons | official PTBot feature and video | not started as an explicit user tool | partial by detected chain grouping only | add similarity grouping from strand / duct / chair metadata; extend grouped package output |
| Align tendon bubbles | official PTBot feature and video | started with generated bubble stacking and spacing logic | started with generated bubble stacking and spacing logic | add true reference-based alignment to guide lines / grids / baselines |
| Dimension tendons | official PTBot feature and video | partial with baseline profile dimension chains | partial with generated extent, segment, and overall dimensions in CAD chain-detail views | expand dimension automation across all generated PT package views and later add guided references |
| Renumber tendons | official PTBot feature | strong import-time baseline with prefix, digits, start number, sequence modes, and snapshot audit | strong import-time baseline with optional CAD-label preservation plus shared renumber logic | add a dedicated post-import renumber command over grouped tendon objects and CAD chains |
| Show or hide intermediate chair heights | official PTBot feature and video | not started | not started | add chair-height metadata extraction first, then annotation toggles |
| Resolve text clash | official PTBot feature | started with first-pass key-point label cleanup | started with first-pass chain-label cleanup | add rerunnable clash resolution and unresolved-marker output |
| Review tendon clash | official PTBot feature | not started | limited visual review only | add tendon-vs-tendon clash scan over direct tendon solids |
| Review chair clash | official PTBot feature and video | not started | not started | depends on chair/support metadata becoming available |
| Create 3D view | official PTBot feature and video | implemented baseline PT review package with 3D tendon output | implemented baseline CAD PT 3D review output | refine the generated inspection 3D view and later add group-focused QA views and error summaries |
| Material takeoff | official PTBot feature and video | implemented baseline takeoff sheet plus normalized JSON / CSV snapshot export | implemented baseline takeoff sheet plus normalized JSON / CSV snapshot export | evolve takeoff rows into richer quantity records and schedule-like deliverables |
| Fast PT shop drawings | repeated channel theme | partial but becoming credible | partial but useful as a fallback workflow | unify profile sheets, CAD sheets, takeoff sheets, dimensions, cleanup passes, and review outputs into one production package |

## Current Read Of Our Product Against The Channel

### What already aligns well

- two-option import strategy is correct
- direct ADAPT import is the right flagship path
- CAD fallback is the right trust-preserving path
- generated drafting views are moving us beyond plain imported linework
- PT takeoff sheets now exist in both workflows
- both workflows can now emit one internal PT snapshot contract for later audit / renumber / quantity tools
- 3D review output now exists in both workflows
- first-stage bubble alignment and text cleanup have started instead of staying only on the roadmap

### What is still behind the channel benchmark

- tendon objects are not yet full smart PT objects
- renumbering is still mostly import-time, not a separate user-driven editing command
- dimensions are still not complete across every generated PT package view
- bubble alignment is not yet reference-driven the way PTBot shows it
- clash review is still not available as an explicit PT QA tool
- chair-based detailing is still missing

## Recommended Implementation Order

If we follow the channel closely, the best order is:

1. `Smart tendon metadata`
   - direct ADAPT path should keep collecting strand, duct, tendon type, and end-condition data
   - this is the base for every later tool

2. `Renumber + mark management`
   - users need stable tendon marks before dimensioning and sheets become efficient
   - this should work for both direct tendons and CAD-derived chains

3. `Reference-driven bubble alignment`
   - extend current stacking logic into true align-to-grid / guide-line behavior
   - this is the clearest remaining gap against the reviewed PTBot videos

4. `Dimension generation`
   - complete PT drafting dimensions across all generated views
   - keep the CAD chain-detail workflow as the proving ground, then mirror the behavior in direct profiles

5. `Text clash reduction`
   - evolve the current first-pass cleanup into a rerunnable review tool
   - make this work after renumbering, dimensioning, or chair-label generation

6. `3D inspection view generation`
   - create package-level or tendon-group-level 3D review views
   - this supports quality review and later clash checking

7. `Clash review`
   - tendon clash first
   - chair / support clash after metadata exists

8. `Chair-detail controls`
   - show / hide intermediate chair heights
   - only worth building after chair metadata is trustworthy

## Best Mapping To Our Two Options

### Option 1: Direct ADAPT import

This path should become our closest equivalent to the PTBot Revit workflow.

Best-fit channel features for Option 1:

- smart tendon objects
- renumbering
- dimensioning
- bubble alignment
- 3D inspection
- clash review
- material takeoff
- final PT shop drawing packaging

### Option 2: DWG / DXF import

This path should stay useful, but it should be honest about its limits.

Best-fit channel features for Option 2:

- imported drawing reference
- chain detection
- mark assignment
- chain-detail views
- dimensioning over extracted chains
- takeoff from detected chains
- sheet packaging for review / tracing / transition workflows

Less suitable for Option 2 unless enriched later:

- chair intelligence
- true tendon clash detection
- rich smart-object editing

## Code Areas To Use Next

### Direct ADAPT path

- `ReadAdaptAdmTendonGeometry(...)`
- `ImportAdaptTendonProfiles(...)`
- `BuildAdaptProfileTakeoffRows(...)`
- `SyncAdaptProfileDraftingViews(...)`
- `DrawAdaptProfileDraftingView(...)`

### DWG / DXF path

- `BuildAdaptCadPtPackage(...)`
- `BuildAdaptCadPreviewChains(...)`
- `BuildAdaptCadPtTakeoffRows(...)`
- `DrawAdaptCadPtDraftingView(...)`
- `DrawAdaptCadPtChainDraftingView(...)`

### Shared PT package layer

- sheet naming and packaging helpers
- takeoff rendering helpers
- future renumber / dimension / annotation alignment services
- future tendon-signature and linked-group services
- future chair/support review services
- future chair/support presentation-mode services

## Practical Product Direction

Following the `mindCreations` channel does not mean we should only chase visuals.

The strongest lesson from the channel is that the winning PT workflow in Revit is:

- import
- objectify
- annotate
- check
- quantify
- package

That is the standard `DRAWING PT` should keep following.

The newer demo video `PTBot Revit Demo - PT Shop Drawings in minutes` reinforces one more practical build rule:

- every PT feature should be judged by whether it reduces shop-drawing cleanup time after import
