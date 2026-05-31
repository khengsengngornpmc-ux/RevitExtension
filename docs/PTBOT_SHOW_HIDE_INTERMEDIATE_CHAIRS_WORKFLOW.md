# PTBot Show/Hide Intermediate Chairs Workflow

This note studies the video:

- `PTBot Revit - Show/Hide Intermediate Chairs`
- `https://www.youtube.com/watch?v=PXbH-jHN1vk`
- published by `mindCreations` on `September 21, 2020`

Public description:

- `How to show or hide intermediate chairs in PTBot Revit.`

The PTBot product page adds the key intent:

- show or hide intermediary chair heights for selected tendons to add or reduce detail where needed

The purpose of this note is to translate that workflow into our `DRAWING PT` design direction.

## Why This Video Matters

This reference is important because it shows a very practical PT drafting principle:

- not every sheet needs the same amount of chair detail

That means PTBot is not only creating information.

It is also controlling `how much` information is shown to suit the drawing purpose.

For our project, this matters because a strong PT workflow should be able to move between:

- review clarity
- shop-detail richness
- drawing simplicity

without rebuilding everything manually.

## Core Concept

The feature is not simply `show chairs`.

It is:

- show or hide `intermediate` chair heights
- for `selected tendons`
- to `add or reduce detail where needed`

That means the core idea is `detail-level control`.

This is valuable because PT drawings can become noisy very quickly if every intermediate support value is always displayed.

## What The Workflow Implies

The `Show/Hide Intermediate Chairs` workflow suggests this sequence:

1. import or create tendon objects
2. have chair/support information associated with those tendons
3. select the tendons relevant to the current drawing purpose
4. switch intermediate chair detail on or off
5. keep the drawing readable while preserving the ability to show more detail when needed

That means chair visibility is a `presentation control layer`.

It sits after:

- smart tendon creation
- support/chair awareness

and before:

- final sheet cleanup
- issue-specific detailing

## Why This Is Different From Chair Clash

Chair clash is about checking correctness.

Intermediate-chair visibility is about controlling presentation density.

That means the two features are related, but not the same:

- chair clash belongs to checking/review
- show/hide intermediate chairs belongs to drawing-detail control

For our project, both should eventually share the same support/chair metadata foundation.

## Best Translation To Our Two Options

### Option 1: Direct ADAPT Import

This is the correct home for the full feature.

Why:

- direct import is where chair/support logic can become structured and reliable
- the direct path is our flagship smart-workflow path

For direct ADAPT, the long-term capability should include:

- support/chair extraction or inference
- display toggles for intermediate chair heights
- different chair-detail modes per drawing or package view

This is where we can become most PTBot-like.

### Option 2: DWG / DXF Import

This path can support only a weaker version.

Why:

- CAD fallback usually has weak or inconsistent chair intelligence
- chair markers may exist only as graphics or labels

For DWG/DXF, a realistic version may be:

- detect likely support labels
- hide or show certain detected annotation groups
- provide visual filtering rather than full smart chair-detail logic

So the honest product rule is:

- intermediate-chair controls belong primarily to `Option 1`

## Deep Workflow Breakdown For Our Project

### Step 1. Preserve chair/support information during import

The feature cannot exist if support/chair information disappears during parsing.

That means future direct import should preserve anything related to:

- support points
- chair heights
- high/low point behavior
- source labels or table fields that imply support detail

### Step 2. Build an internal support/chair model

We need a reliable internal representation before we can toggle anything.

Possible fields:

- tendon group id
- path position or station
- chair height
- support category
- source confidence
- visibility role

This is the same foundation needed for chair-clash review.

### Step 3. Distinguish primary and intermediate chairs

The PTBot wording specifically refers to `intermediate` chairs.

That means the system should be able to tell the difference between:

- key support points
- intermediate support points

This is crucial because hiding all chairs would be too destructive, while hiding only intermediate ones can reduce clutter without losing important layout understanding.

### Step 4. Apply view-level detail control

The visibility control should likely be view- or package-specific.

Possible future modes:

- full chair detail
- key chairs only
- no intermediate chair heights

This makes the same imported tendon data useful for different documentation stages.

### Step 5. Keep labels and spacing readable

The point of hiding intermediate chair heights is not only fewer objects.

It is a cleaner drawing.

That means this feature connects strongly with:

- text clash reduction
- bubble alignment
- dimension clarity

## What This Means For Our Code

Today we are not chair-aware yet, so this feature is not immediately ready to implement.

But this video tells us how the future chair/detail layer should behave once the metadata exists.

That means the eventual code will need:

- chair/support internal objects
- primary vs intermediate classification
- visibility flags or view rules
- drawing refresh behavior after toggles

## Best Code Areas To Extend First

### Direct ADAPT path

- `ReadAdaptAdmTendonGeometry(...)`
- `ReadAdaptTendonProfileRows(...)`
- `ImportAdaptTendonProfiles(...)`
- `DrawAdaptProfileDraftingView(...)`

### Shared PT workflow layer

- future support/chair model classes
- future chair visibility rules
- future detail-level presentation helpers

## Recommended Internal Design

If we follow this PTBot method well, we should eventually add something like:

1. `PtSupportPoint`
   - support/chair data for one tendon position

2. `PtChairDisplayMode`
   - `Full`
   - `KeyOnly`
   - `HideIntermediate`

3. `ClassifyPtSupportPoints(...)`
   - decide which supports are key and which are intermediate

4. `ApplyPtChairDisplayMode(...)`
   - update drafting/review output based on selected detail mode

## Recommended Development Order

If we follow this reference correctly, the chair-detail path should be built like this:

1. strengthen smart metadata extraction
2. add support/chair internal representation
3. add primary vs intermediate classification
4. add show/hide display modes in profile and review views
5. connect this to text-clash and drawing-cleanup logic later

## What This Video Changes In Our Priority

This reference does not move ahead of:

- renumber
- dimensions
- basic smart metadata

But it does make the future chair/detail layer much clearer.

It tells us that chair support logic should not only exist for clash checking.

It should also exist for `detail-level control`.

## Bottom Line

The `PTBot Revit - Show/Hide Intermediate Chairs` video shows that PTBot gives users control over how much chair detail appears in the PT drawing.

For our project, this means:

- direct ADAPT import should eventually support chair/support-aware presentation modes
- intermediate chair visibility should be adjustable without rebuilding the tendon package
- the same support/chair metadata should later serve both clash review and drawing-detail control
