# PTBot Create 3D View Workflow

This note studies the video:

- `PTBot Revit - Create 3D View`
- `https://www.youtube.com/watch?v=VErnlrXrlpw`
- published by `mindCreations` on `September 21, 2020`

Public description:

- `How to create 3D view in PTBot Revit.`

The PTBot product page adds the key intent:

- the model can be visualised in 3D to inspect segment elevations and detect any issues not apparent in the 2D view

The purpose of this note is to translate that workflow into our `DRAWING PT` design direction.

## Why This Video Matters

This reference is important because it explains why the PT 3D view exists.

It is not only for presentation.

It is for:

- reviewing segment elevations
- finding issues hidden in 2D
- understanding tendon geometry more clearly

That means the 3D view belongs to the `checking and review` layer of the workflow, not only the drawing layer.

## Core Concept

The PTBot wording is very clear:

- visualise the model in 3D
- inspect segment elevations
- detect issues not apparent in 2D

That means a good PT 3D view should not be treated as a generic model view.

It should be optimized for:

- tendon geometry understanding
- elevation awareness
- issue spotting

## What The Workflow Implies

The `Create 3D View` workflow suggests this sequence:

1. import or create tendon objects
2. isolate the relevant PT content in a dedicated 3D view
3. use the view to inspect tendon geometry spatially
4. check segment elevation changes and problem areas
5. use that review to support later drafting and coordination decisions

That means the 3D view is a `review tool`, not only a deliverable.

## Why 3D Matters Even If 2D Views Exist

PT workflows often rely heavily on plan and profile graphics.

But the PTBot wording highlights a real limitation of 2D:

- some issues are not apparent there

For our project, this means 3D review should help users catch:

- elevation misunderstandings
- unexpected geometry changes
- confusing tendon paths
- early clash-like conditions
- unusual segment transitions

## Best Translation To Our Two Options

### Option 1: Direct ADAPT Import

This is the best place for the strongest 3D-view workflow.

Why:

- direct import creates the best native PT geometry
- this path is our flagship smart-workflow path
- 3D elevation review is especially valuable when the tendon data is truly modeled

For direct ADAPT, the long-term 3D review should support:

- source package 3D review sheets
- per-group 3D inspection views later
- highlighted issue zones later
- support/chair review later

### Option 2: DWG / DXF Import

This path can support a lighter but still useful 3D review.

Why:

- CAD imports still benefit from isolated package-level review
- a 3D view can help users understand the imported context and chain layout package

But this path is weaker because:

- CAD fallback is less semantically rich
- 3D review is more about package inspection than true smart-tendon review

## Deep Workflow Breakdown For Our Project

### Step 1. Build a dedicated PT review view

The 3D view should not be a generic model dump.

It should focus on the PT package that was just created.

That means:

- isolate the PT geometry
- frame it with a useful section box
- give it a predictable name

This is already partly aligned with our current implementation.

## Step 2. Optimize the view for elevation understanding

The PTBot wording specifically mentions `segment elevations`.

So the view should help the user read vertical changes clearly.

That suggests future refinement such as:

- better view orientation
- stronger visual contrast for tendon objects
- optional group-specific views
- issue highlighting later

## Step 3. Use the 3D view as a problem-finding tool

The value is not only that the view exists.

The value is that it reveals issues that 2D may hide.

That means the 3D review layer should eventually support:

- tendon geometry sanity checking
- tendon clash review
- chair/support review later
- quick confirmation before issuing PT sheets

## Step 4. Connect the 3D view to the PT package

PTBot treats the 3D view as part of the workflow.

For our project, that means the 3D review should be part of:

- direct ADAPT PT package output
- CAD PT package output
- future per-group review output

This is already the right direction in our current code.

## What This Means For Our Code

We already implemented a baseline version of this capability:

- direct ADAPT `PT model review sheet`
- CAD `PT model review sheet`

So this video does not introduce a missing category.

It tells us how to refine the category we already started.

## Best Code Areas To Extend

### Shared PT package layer

- `CreateShopDrawingModelView(...)`
- `CreateShopDrawingSheetForViews(...)`

### Direct ADAPT path

- `SyncAdaptProfileModelReviewSheet(...)`

### DWG / DXF path

- `SyncAdaptCadPtModelReviewSheet(...)`

## Recommended Internal Design Direction

If we follow this PTBot method more closely, the next refinements should be:

1. better PT-specific 3D naming and view settings
2. stronger visual emphasis on tendon geometry
3. optional per-group or per-zone 3D inspection views
4. future issue highlighting inside the 3D review

## Recommended Development Order

This reference does not move ahead of:

- renumber
- dimensions
- bubble alignment

because we already have a baseline 3D review feature.

Instead, it clarifies the refinement path for what we already built:

1. keep current package-level 3D review sheets
2. later add per-group 3D inspection
3. later connect clash and chair review to those views

## Bottom Line

The `PTBot Revit - Create 3D View` video shows that PT 3D views are valuable because they reveal elevation and geometry issues that 2D views may hide.

For our project, this means:

- our new PT model review sheets are the correct direction
- the next goal is to make them more PT-specific and more inspection-oriented
- future clash and chair-support review should build on top of this 3D review layer
