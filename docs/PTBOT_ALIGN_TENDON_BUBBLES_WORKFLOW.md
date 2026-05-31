# PTBot Align Tendon Bubbles Workflow

This note studies the video:

- `PTBot Revit - Align Tendon Bubbles`
- `https://www.youtube.com/watch?v=UHUswSVqNb8`
- published by `mindCreations` on `September 21, 2020`

Public description:

- `How to align tendon bubbles in PTBot Revit.`

The PTBot product page adds the important summary:

- align selected tendon bubbles to an existing grid, reference plane or detail line

The purpose of this note is to translate that workflow into the design direction for `DRAWING PT`.

## Why This Video Matters

This reference is very important for the `drawing cleanup` side of PTBot.

Why:

- tendon bubbles are one of the most visible PT annotations
- badly placed bubbles make the sheet look crowded and hard to read
- aligned bubbles make the drawing feel deliberate and coordinated

This is not only a cosmetic feature.

It is a readability and production-speed feature.

## Core Concept

The PTBot wording gives a very clear method:

- align `selected` tendon bubbles
- align them to an `existing grid`, `reference plane`, or `detail line`

That means the workflow is not random auto-layout.

It is controlled alignment to a meaningful drafting reference.

This is strong because it connects annotation placement back to the drawing structure.

## What The Workflow Implies

The `Align Tendon Bubbles` workflow suggests this sequence:

1. import or create tendon objects
2. generate or expose tendon bubbles
3. select the bubbles that should be cleaned up
4. choose a reliable drawing reference
5. align those bubbles to the reference
6. keep leaders and labels readable after alignment

That means bubble alignment is a `post-annotation cleanup layer`.

It sits after:

- import
- mark identity
- basic bubble creation

and before:

- final drawing issue

## Why Reference-Based Alignment Matters

The feature uses references such as:

- grid
- reference plane
- detail line

That tells us the alignment should be tied to explicit drafting structure, not just free-positioned by eye.

For our project, this is important because it suggests a practical implementation path:

- use an explicit baseline or guide line
- offset bubbles consistently from that baseline
- preserve readable leader connections to the tendon graphics

## Best Translation To Our Two Options

### Option 1: Direct ADAPT Import

This is the best place for the full feature.

Why:

- direct import can eventually create smarter bubble identities per tendon
- linked-group and renumber logic can later work with bubble placement
- this is our flagship smart-workflow path

For direct ADAPT, bubble alignment should eventually support:

- profile-view bubble alignment
- plan/package-view bubble alignment
- group-aware bubble alignment
- linked-tendon-aware bubble alignment later

### Option 2: DWG / DXF Import

This path is a strong early implementation target.

Why:

- CAD chain-detail views already generate PT marks and isolated review graphics
- those views are a good place to experiment with bubble placement rules

For DWG/DXF, bubble alignment should focus on:

- chain-detail views
- summary drafting views
- alignment to a chosen guide line or calculated layout edge

This may be easier to implement first even if direct ADAPT is the long-term premium path.

## Deep Workflow Breakdown For Our Project

### Step 1. Create stable bubble identity

Bubble alignment only works if each bubble belongs to a known tendon or chain mark.

For us, that means bubble logic should attach to:

- direct ADAPT shop marks
- CAD chain marks

This is why renumber and mark stability matter first.

## Step 2. Represent bubble geometry explicitly

We need a consistent internal model of each bubble placement.

Possible fields:

- tendon or chain id
- label text
- anchor point on tendon geometry
- current bubble position
- leader path
- target alignment line

Without this layer, alignment becomes harder to control.

## Step 3. Choose an alignment reference

This is the most PTBot-like part of the method.

Possible references for our future implementation:

- an explicit guide line in the drafting view
- a calculated side reference from the tendon layout extents
- a grid or reference plane when working in plan-like views

This gives the alignment a repeatable rule.

## Step 4. Reposition bubbles consistently

After a reference is chosen, bubbles should be shifted so they:

- sit on a common baseline
- preserve a readable order
- avoid collapsing into each other

This should work with minimum spacing rules between bubble centers.

## Step 5. Update leaders and label clarity

Alignment is not useful if the leaders become messy afterward.

So the workflow should also:

- re-route leaders cleanly
- maintain readable label distances
- work with later text-clash reduction

This is where bubble alignment and text-clash logic connect.

## Step 6. Integrate into PT package generation

Once bubble alignment exists, it should become part of the PT package workflow rather than a disconnected tool.

That means:

- generated views should support alignment rules
- future reruns should preserve or rebuild alignment predictably
- aligned bubbles should improve the final sheet output directly

## What This Means For Our Code

Today we already create PT marks and bubble-like graphics in the direct profile workflow.

We do not yet have a true bubble-alignment service.

That means the missing pieces are:

- explicit bubble placement data
- alignment reference logic
- leader rerouting after alignment

## Best Code Areas To Extend

### Direct ADAPT path

- `DrawAdaptProfileDraftingView(...)`
- `DrawAdaptProfileShopMarkBubbles(...)`
- `GroupAdaptTendonSegments(...)`

### DWG / DXF path

- `DrawAdaptCadPtDraftingView(...)`
- `DrawAdaptCadPtChainDraftingView(...)`

### Shared PT drafting layer

- future bubble layout helpers
- future alignment reference helpers
- future leader rerouting logic

## Recommended Internal Design

If we follow this PTBot method well, we should eventually add something like:

1. `PtBubbleLayout`
   - tendon id or chain id
   - mark text
   - anchor point
   - bubble point
   - leader points

2. `PtBubbleAlignmentReference`
   - reference type
   - line or axis data
   - offset rule

3. `AlignPtBubbles(...)`
   - align selected bubble layouts to a chosen reference

4. `ResolvePtBubbleOverlap(...)`
   - enforce minimum spacing after alignment

## Recommended Development Order

If we follow this reference closely, the best order is:

1. finish `renumber + mark management`
2. add `dimension generation`
3. add bubble-alignment data and alignment helpers
4. connect bubble alignment with text-clash reduction later

Why:

- stable marks are needed before bubble cleanup is useful
- dimensions and bubble cleanup work closely together in final PT drafting

## What This Video Changes In Our Priority

This reference confirms that `bubble alignment` should remain immediately after dimensions in the roadmap.

It does not jump ahead of:

- renumber
- dimensions

But it gives us a clear target for the next drafting-cleanup layer after those two are in place.

## Bottom Line

The `PTBot Revit - Align Tendon Bubbles` video shows that strong PT drafting is not only about generating bubbles.

It is about aligning those bubbles to meaningful drawing references so the sheet becomes easier to read and faster to finish.

For our project, this means:

- bubble alignment should become a real PT drafting service
- CAD chain-detail and summary views are a good early implementation target
- direct ADAPT package views should become the premium long-term bubble-alignment workflow
