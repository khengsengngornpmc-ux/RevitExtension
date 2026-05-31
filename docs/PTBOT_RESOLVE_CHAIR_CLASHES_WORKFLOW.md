# PTBot Resolve Chair Clashes Workflow

This note studies the video:

- `PTBot Revit - Resolve Chair Clashes`
- `https://www.youtube.com/watch?v=DeOb76jq-jA`
- published by `mindCreations` on `September 21, 2020`

Public description:

- `How to resolve chair clashes with PTBot Revit.`

The purpose of this note is to translate that workflow into our `DRAWING PT` design direction.

## Why This Video Matters

This reference is important because it shows that PTBot does not only treat tendons as objects that need drafting.

It also treats `chairs` and `support conditions` as part of the PT coordination model.

That is a bigger workflow idea than just fixing one clash.

It means:

- the PT system is not complete when tendon lines are visible
- the PT system is more complete when support conditions can be reviewed and corrected too

For our project, this matters because chair logic is one of the clearest missing layers today.

## Core Concept

The PTBot product page and related features suggest a connected chair workflow:

- show or hide intermediate chair heights
- resolve text clash for chair labels
- review chair clashes

That means chairs are not incidental data.

They are treated as:

- modeled or implied PT support points
- reviewable coordination items
- drafting-relevant annotations

For our project, the key lesson is:

- tendon import should eventually support chair-awareness

## What The Workflow Implies

The `Resolve Chair Clashes` workflow suggests a sequence like this:

1. import tendon information
2. identify chair/support conditions
3. visualize them in the Revit PT workflow
4. detect problematic overlap or conflict
5. adjust or review the layout
6. reflect the result in drafting output

That means chair clash is not an isolated feature.

It depends on several earlier capabilities:

- smart tendon data
- support/chair metadata
- review views
- repeatable update behavior

## Why This Is Harder Than Tendon Clash

Tendon clash can often be approximated from direct geometry overlap.

Chair clash is harder because it depends on support interpretation.

For our project, possible inputs may include:

- explicit chair/support rows from structured source data
- inferred support points from tendon profile geometry
- source labels that imply high/low support locations
- future user-edited support markers

So the first challenge is not the clash check itself.

The first challenge is:

- creating a reliable chair/support representation

## Best Translation To Our Two Options

### Option 1: Direct ADAPT Import

This is the correct home for the full chair-clash workflow.

Why:

- direct import is where structured PT metadata has the best chance to exist
- the direct path is our flagship smart-workflow path
- chair support logic belongs with smart tendon behavior, not only with imported linework

For direct ADAPT, the long-term chair workflow should include:

- support/chair metadata extraction when available
- inferred support/chair markers when metadata is incomplete
- chair review in 3D and profile views
- chair clash detection
- chair-aware drafting output

### Option 2: DWG / DXF Import

This path can support a very limited version only.

Why:

- CAD fallback usually does not carry explicit chair intelligence
- even if chair graphics exist, they may be weakly structured

For DWG/DXF, realistic support may be limited to:

- detecting likely support markers from CAD labels or symbols
- using chain geometry to infer possible support locations
- offering only review hints, not full smart chair coordination

So the honest product rule is:

- chair clash belongs primarily to `Option 1`

## Deep Workflow Breakdown For Our Project

### Step 1. Import tendons with support-aware metadata in mind

If we want chair clash later, our import should preserve anything that may describe support conditions:

- tendon family naming
- source label text
- high/low point sequence
- source table columns that imply support or chair values

This means metadata extraction should not be kept minimal.

### Step 2. Create a chair/support representation

Before clash review can happen, we need a consistent internal model of a chair or support condition.

Possible fields:

- tendon group id
- station or local path position
- elevation
- support height
- support type
- source confidence

Without this layer, chair clash review cannot be reliable.

### Step 3. Visualize chairs in review views

The user needs to see the support conditions clearly.

That means chairs should eventually appear in:

- profile drafting views
- 3D review views
- possibly chain-detail views for CAD fallback

This is what turns the hidden metadata into a practical review workflow.

### Step 4. Detect clashes or crowded conditions

Once chair/support objects exist, the system can check for:

- support points too close together
- overlapping support markers
- impossible or conflicting local support conditions
- repeated crowded annotation in the same zone

This can begin with rule-based checks before becoming more advanced.

### Step 5. Provide actionable resolution output

A useful clash workflow should not only say there is a problem.

It should help the user find and fix it.

That suggests outputs like:

- flagged chair/support pairs
- issue lists in a review view
- highlighted elements in 3D
- notes in the PT package

### Step 6. Flow the resolved state into drafting

The downstream value appears when the reviewed chair logic affects:

- final annotation clarity
- intermediate chair height display
- reduced drawing clutter
- more accurate PT packages

This is how a coordination feature becomes a shop-drawing speed feature too.

## What This Means For Our Code

Today we are not chair-aware yet.

That means we currently lack:

- chair/support metadata extraction
- chair/support internal objects
- chair review graphics
- chair clash checking

So this video does not suggest a tiny patch.

It suggests a whole future sub-layer.

## Best Code Areas To Extend First

### Direct ADAPT path

- `ReadAdaptAdmTendonGeometry(...)`
- `ReadAdaptTendonProfileRows(...)`
- `ImportAdaptTendonProfiles(...)`
- `DrawAdaptProfileDraftingView(...)`

### Shared PT workflow layer

- future support/chair model classes
- future chair-clash checker
- future review annotation helpers

### Review layer

- 3D PT model review sheets
- future per-tendon or per-group review views

## Recommended Internal Design

If we follow this reference well, we should eventually add something like:

1. `PtSupportPoint`
   - station
   - elevation
   - height
   - source confidence
   - tendon group id

2. `PtChairReviewIssue`
   - issue type
   - involved support points
   - message
   - severity

3. `BuildPtSupportPoints(...)`
   - extract or infer support/chair data from imported tendons

4. `DetectPtChairClashes(...)`
   - detect conflicting support conditions

5. `DrawPtChairReview(...)`
   - show issues in profile or 3D review output

## Recommended Development Order

If we follow this PTBot reference correctly, the chair path should be built in this order:

1. strengthen direct ADAPT metadata extraction
2. add support/chair internal representation
3. add support/chair visualization in review views
4. add chair clash rules
5. add chair-aware drafting controls

This is why chair clash is not the next immediate feature before metadata work.

It depends on the smart-import foundation first.

## What This Video Changes In Our Priority

This reference confirms that `chair/detail intelligence` is real product value, but it does not move ahead of:

- smart metadata
- renumber / marks
- dimensions

Instead, it clarifies a later phase:

- after we strengthen smart tendon data, we should build chair-awareness as part of the checking layer

## Bottom Line

The `PTBot Revit - Resolve Chair Clashes` video shows that PTBot treats chairs as coordination objects, not just secondary drafting labels.

For our project, this means:

- chair/support awareness should eventually exist in the direct ADAPT workflow
- chair clash is a future smart-review capability built on top of strong metadata
- `DWG/DXF` can only support a weak fallback version
- the long-term direct-import target should include both tendon review and chair/support review
