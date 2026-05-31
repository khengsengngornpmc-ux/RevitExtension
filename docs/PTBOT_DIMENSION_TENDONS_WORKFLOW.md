# PTBot Dimension Tendons Workflow

This note studies the video:

- `PTBot Revit - Dimension Tendons`
- `https://www.youtube.com/watch?v=KMso7ZJSDow`
- published by `mindCreations` on `September 21, 2020`

Public description:

- `How to dimension tendons in PTBot Revit.`

The PTBot product page adds an important summary of the feature:

- dimensioning can be added between selected parallel tendons to reduce complexity in measurements

The purpose of this note is to translate that workflow into the design direction for `DRAWING PT`.

## Why This Video Matters

This reference is one of the most important PTBot videos for our next build stage.

Why:

- tendon dimensions are central to shop drawing usefulness
- dimensions are one of the clearest time-saving automation features
- dimensions connect imported tendon geometry to real construction communication

Without good dimensions, imported tendons are still only partway to a finished PT package.

## Core Concept

The PTBot wording tells us something very specific:

- dimensions are added between `selected parallel tendons`
- the purpose is to `reduce complexity in measurements`

That means the workflow is not:

- dimension everything blindly

It is:

- detect or select a meaningful tendon set
- apply dimensions where they simplify the reading of the layout

This is a strong drafting principle, not only a software action.

## What The Workflow Implies

The `Dimension Tendons` workflow suggests this sequence:

1. import or create tendon objects in Revit
2. identify a tendon set that should be dimensioned together
3. understand which tendons are parallel or comparable
4. build dimensions between them
5. keep the resulting dimensions readable and less complex than manual measuring

That means dimensioning is a `smart drafting layer`.

It sits after:

- import
- object creation
- basic grouping

and before final package cleanup.

## Why Parallelism Matters

The phrase `selected parallel tendons` is very important.

It suggests the feature is not generic free-form dimensioning.

It is controlled dimensioning over tendons that share a strong layout relationship.

For our project, that means we should evaluate:

- direction similarity
- spacing consistency
- proximity
- group membership
- linked-family membership later

Parallelism is really a geometric filter that makes the resulting dimensions understandable.

## Best Translation To Our Two Options

### Option 1: Direct ADAPT Import

This is the best place for the full `Dimension Tendons` idea.

Why:

- direct import gives cleaner structured geometry
- metadata can later support better grouping and selection
- this is our flagship smart-workflow path

For direct ADAPT, dimensioning should eventually support:

- profile-based dimensions
- plan-based tendon spacing dimensions
- group-aware dimensions
- linked-tendon-group dimensions later

### Option 2: DWG / DXF Import

This path can support a practical early version.

Why:

- CAD chain-detail views already isolate tendon-like paths
- those views are a good place to add first-pass dimension automation

For DWG/DXF, dimensioning should focus on:

- chain spacing
- chain extent / local layout distances
- dimensions in chain-detail or summary drafting views

This is probably the easiest first implementation path even if direct ADAPT is the long-term premium path.

## Deep Workflow Breakdown For Our Project

### Step 1. Start from clean tendon or chain representations

Dimensioning only works well if the underlying tendon objects are already organized.

For us, that means:

- direct path uses grouped `AdaptTendonProfileSegmentPayload`
- CAD path uses `AdaptCadPreviewChain`

This foundation already exists.

## Step 2. Identify dimension candidates

The system should not dimension every tendon against every other tendon.

It should find useful candidate sets, for example:

- tendons inside one layout band
- tendons sharing a similar direction
- chains within one CAD layer or one localized zone
- linked or similar tendon groups later

This is the selection-intelligence layer.

## Step 3. Detect parallel or near-parallel behavior

This is a core PTBot idea from the product-page wording.

Two tendons should be treated as a dimension pair or set when they are sufficiently parallel.

Possible checks:

- compare main direction vectors
- allow a reasonable angular tolerance
- ensure overlapping projected range
- exclude isolated outliers

This can start simple and improve later.

## Step 4. Choose dimension anchors

Good tendon dimensions need stable anchor locations.

Possible anchors include:

- centerline offsets
- support points later
- end points
- projected spacing lines in a drafting view

For our project, this is especially important because profile views and plan-like CAD views will need different anchor logic.

## Step 5. Place dimensions where they simplify the drawing

This is the drafting-intelligence part.

The goal is not just to create dimensions.

The goal is to reduce reading complexity.

That means:

- avoid dimensioning everything
- prefer representative spacing dimensions
- cluster dimensions logically
- keep them outside the densest tendon graphics when possible

This is how the feature becomes PTBot-like instead of generic.

## Step 6. Integrate dimensions into the PT package

Once dimensions are created, they should become part of the PT package output, not an isolated experiment.

That means they should appear in:

- generated chain-detail views
- generated summary views
- future direct-import plan/package views
- possibly dedicated dimension sheets later

## What This Means For Our Code

Today we do not yet have tendon dimension generation in the PT workflow.

But we do already have the best starting points:

- isolated CAD chain-detail drafting views
- direct ADAPT profile drafting views
- grouped tendon and chain data
- shop-drawing packaging helpers

So dimensioning is one of the clearest next real features.

## Best Code Areas To Extend

### Direct ADAPT path

- `DrawAdaptProfileDraftingView(...)`
- `BuildAdaptOrderedPathPoints(...)`
- `GroupAdaptTendonSegments(...)`

### DWG / DXF path

- `DrawAdaptCadPtDraftingView(...)`
- `DrawAdaptCadPtChainDraftingView(...)`
- `BuildAdaptCadPreviewChains(...)`

### Shared PT drafting layer

- future tendon-direction helpers
- future dimension-candidate builder
- future dimension placement helpers

## Recommended Internal Design

If we follow this PTBot method well, we should eventually add something like:

1. `PtDimensionCandidate`
   - tendon or chain ids
   - primary direction
   - spacing basis
   - candidate anchor points

2. `BuildPtDimensionCandidates(...)`
   - find useful parallel tendon sets

3. `PlacePtSpacingDimensions(...)`
   - create dimensions between selected or auto-detected tendon sets

4. `DrawPtDimensionGuides(...)`
   - optional helper graphics or layout references in drafting views

## Recommended Implementation Order

If we follow this reference closely, the best order is:

1. finish `renumber + mark management`
2. build CAD-chain dimensioning first
3. add direct ADAPT package dimensioning next
4. later make dimensions group-aware and linked-tendon-aware

Why this order:

- CAD chain-detail views already isolate geometry cleanly
- they are easier to dimension first
- direct ADAPT should still be the stronger long-term workflow

## What This Video Changes In Our Priority

This reference strongly confirms that `dimension generation` should be one of the next core PT features.

It does not replace `renumber + mark management`.

It tells us what should come immediately after, or be designed in parallel with it.

The practical planning rule becomes:

- renumber first
- dimensions next
- bubble alignment after those two

## Bottom Line

The `PTBot Revit - Dimension Tendons` video shows that a strong PT workflow uses dimension automation to simplify tendon measurement, especially between selected parallel tendons.

For our project, this means:

- dimensions should become a first-class PT package feature
- CAD chain-detail views are a good first implementation target
- direct ADAPT package views should become the premium long-term dimension workflow
- dimensioning should reduce drawing complexity, not add more clutter
