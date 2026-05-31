# PTBot Link Tendons Workflow

This note studies the video:

- `PTBot Revit - Link Tendons`
- `https://www.youtube.com/watch?v=FtDB4femkZs`
- published by `mindCreations` on `September 21, 2020`

Public description:

- `How to link similar tendons in PTBot Revit.`

The purpose of this note is to translate that workflow into a practical method for `DRAWING PT`.

## Why This Video Matters

`Link Tendons` is one of the most important PTBot ideas for our project.

It matters because PT shop drawings become slow when every tendon is treated as a completely isolated object.

The value of linking is:

- reduce repeated annotation
- reduce repeated drafting work
- simplify tendon presentation
- make shop drawing output cleaner
- make similar tendons easier to review together

This is not only a visual feature.

It is a `workflow compression` feature.

## Core Concept

The product-page wording around PTBot explains the intent clearly:

- tendons with the same chair heights can be linked together to simplify the model

That means the purpose of linking is not random grouping.

It is controlled grouping based on similarity.

The similarity can come from metadata such as:

- chair heights
- strand count
- duct size
- tendon type
- similar profile shape
- similar start/end conditions

For our project, the most important lesson is:

- imported tendons should not only exist as geometry
- they should also be comparable objects with attributes

## What The Workflow Implies

The PTBot `Link Tendons` workflow suggests this sequence:

1. import tendons into Revit
2. identify similar tendons
3. link or group them
4. use the grouped result to simplify downstream drafting

That means linking is a `post-import intelligence layer`.

It sits after import, but before:

- bubble alignment
- dimensioning
- renumbering cleanup
- final shop-drawing packaging

This is why the feature is so important for our roadmap.

## What “Link” Should Mean In Our Project

For `DRAWING PT`, linking should not mean joining geometry into one physical tendon.

It should mean creating a drafting and workflow relationship between tendons that are similar enough to be managed together.

In practical terms, linking should support:

- one linked group identity
- one group-level label or summary where appropriate
- cleaner annotation layout
- easier dimensioning strategy
- easier takeoff review
- faster sheet cleanup

## Best Translation To Our Two Options

### Option 1: Direct ADAPT Import

This is the best place for the full `Link Tendons` idea.

Why:

- direct import has the best chance of carrying structured metadata
- grouped ADAPT segments already exist in our code
- this path is our flagship smart-workflow path

For direct ADAPT, the linking engine should eventually compare:

- strand count
- duct diameter
- tendon type
- profile shape similarity
- profile elevation pattern
- source label / tendon family naming

This is the path that can become closest to PTBot behavior.

### Option 2: DWG / DXF Import

This path can support a lighter version of linking.

Why only lighter:

- CAD fallback usually has weaker metadata
- chain detection is shape-driven more than metadata-driven

For DWG/DXF, linking can still be useful by comparing:

- CAD chain length pattern
- local profile/plan geometry similarity
- layer name
- CAD label or mark pattern

So the fallback version should be:

- `link similar detected chains`

not:

- `full smart tendon family grouping`

## Deep Workflow Breakdown For Our Project

### Step 1. Import and normalize tendons

Before linking can work, tendons must already be normalized into a consistent internal representation.

For us, that means:

- direct path uses `AdaptTendonProfileSegmentPayload`
- CAD path uses `AdaptCadPreviewChain`

This step already exists.

## Step 2. Build comparable tendon signatures

Each tendon or chain needs a signature that can be compared.

Example signature fields:

- tendon mark
- strand count
- duct size
- tendon type
- approximate length
- segment count
- profile shape fingerprint
- source family or source label

This is the critical missing layer today.

## Step 3. Score similarity

Two tendons should not be linked only because they look close once.

They should be linked because their signatures match strongly enough.

That suggests a scoring approach:

- exact match on key metadata if available
- tolerance-based comparison on length and geometry
- pattern comparison on profile high/low points

Possible outputs:

- `same family`
- `similar but review manually`
- `not linked`

## Step 4. Create a linked group identity

After similarity is detected, the system should create a stable linked-group identity.

That group identity can later drive:

- shared labels
- shared renumbering pattern
- sheet ordering
- grouped takeoff review
- dimension strategies

This should be a workflow grouping, not a destructive geometry merge.

## Step 5. Use the linked groups in drafting

This is where the PTBot concept becomes valuable.

Once similar tendons are linked, the drafting package can be simplified by:

- showing grouped summaries
- reducing repeated text
- reusing mark logic
- aligning bubbles per group
- dimensioning selected representative tendons instead of every repeated case

This is a major speed gain for shop drawing production.

## Step 6. Preserve manual control

A good linking workflow should not force grouping blindly.

Users should be able to:

- accept auto-linked groups
- review candidate groups
- unlink a wrong grouping
- relink selected tendons manually

This will matter especially when source metadata is incomplete.

## What This Means For Our Code

The current project already has the beginnings of this logic:

- direct tendon grouping
- CAD chain grouping
- PT marks
- takeoff rows
- profile and chain-detail drafting views

But it does not yet have a `similarity-grouping layer`.

That is the missing piece.

## Best Code Areas To Extend

### Direct ADAPT path

- `GroupAdaptTendonSegments(...)`
- `BuildAdaptProfileTakeoffRows(...)`
- `ImportAdaptTendonProfiles(...)`
- `DrawAdaptProfileDraftingView(...)`

### DWG / DXF path

- `BuildAdaptCadPreviewChains(...)`
- `BuildAdaptCadPtTakeoffRows(...)`
- `DrawAdaptCadPtDraftingView(...)`
- `DrawAdaptCadPtChainDraftingView(...)`

### New shared layer we should add

- tendon signature builder
- tendon similarity scorer
- linked-group model
- group-aware drafting helpers

## Recommended Internal Design

If we follow the PTBot method well, we should add something like:

1. `PtTendonSignature`
   - metadata and shape summary for one tendon

2. `PtLinkedTendonGroup`
   - stable linked-group id
   - member tendon ids
   - reason for grouping
   - representative tendon

3. `BuildPtTendonSignatures(...)`
   - create comparable signatures from direct import or CAD chains

4. `LinkSimilarPtTendons(...)`
   - compare signatures and build groups

5. `ApplyPtLinkedGroupsToPackage(...)`
   - use the linked groups in drafting, takeoff, and future annotation

## Recommended Development Order

If we follow this reference strictly, the next implementation steps should be:

1. add tendon signatures for the direct ADAPT path
2. add a similarity/grouping engine
3. surface linked-group summaries in the PT package
4. use linked groups to support renumbering
5. use linked groups to support bubble alignment and dimensions

This is why `Link Tendons` is not a small feature.

It is the bridge between:

- smart import
- and fast shop drawing automation

## What This Video Changes In Our Priority

This reference strengthens the case that our next serious implementation target should be:

- `renumber + mark management`

but with one improvement:

- it should be designed with future linked-group logic in mind

So the best practical interpretation is:

- build renumbering in a way that can later operate on `linked tendon groups`, not only on individual tendons

## Bottom Line

The `PTBot Revit - Link Tendons` video shows that a strong PT workflow is not only about importing tendons into Revit.

It is about reducing repeated drafting effort by recognizing when tendons are similar enough to be managed together.

For our project, this means:

- direct `ADAPT` import should eventually support true linked tendon groups
- `DWG/DXF` should support a lighter chain-linking fallback
- linked groups should become the base for later renumbering, dimensions, bubble alignment, and faster PT shop drawings
