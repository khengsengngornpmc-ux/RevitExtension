# RISA ADAPT Revit Return Workflow

This note studies the RISA reference workflow:

- `Designing Post-Tensioning using Autodesk Revit, ADAPT-Builder and ADAPT-PT/RC`
- `https://www.youtube.com/watch?v=039-YOG4liw`
- RISA video page:
  - `https://risa.com/videos/designing-post-tensioning-using-autodesk-revit-adapt-builder-and-adapt-pt-rc`

Supporting product documentation:

- ADAPT-Builder export options:
  - `https://help.risa.com/risahelp/adaptbuilder/Content/File/export-options.htm`
- ADAPT-Builder product page:
  - `https://risa.com/products/adapt-builder`

## Why This Reference Matters

This is one of the clearest public examples of the workflow we actually want to support:

1. start in `Revit`
2. move the structural model into `ADAPT-Builder`
3. complete PT analysis/design in `ADAPT-PT/RC`
4. bring the PT design result back into `Revit`

That makes this reference different from PTBot.

PTBot is the benchmark for:

- what happens after import inside Revit

This RISA/ADAPT reference is the benchmark for:

- how the designed PT result returns from the design tool into Revit

## Key Process Lesson

The important handoff is not only:

- geometry export

The important handoff is:

- `designed tendon result returns to Revit`

That means our direct ADAPT workflow should aim to import:

- tendon paths
- tendon profile/drape behavior
- tendon identity
- tendon grouping
- tendon metadata suitable for documentation and review

## What The 11:50 Stage Implies

The later stage of the workflow, around the return-to-Revit portion of the video, reinforces this idea:

- Revit is the final coordination and documentation environment
- ADAPT performs the design logic
- the return payload must be rich enough to be useful in BIM and shop drawing workflows

For our project, that means success is not:

- reading an `.adm` file only

Success is:

- turning ADAPT design output into a usable Revit PT package

## Best Translation To Our Project

### Option 1: Direct ADAPT import

This path should become our `design-result import` workflow.

It should own:

- smart tendon geometry
- profile/drape representation
- tendon metadata
- stable tendon marks
- review sheets
- takeoff
- future dimension and annotation automation

### Option 2: DWG / DXF import

This path should remain the fallback workflow for:

- drawing appearance
- reference tracing
- visual confirmation
- chain extraction when direct source interpretation is not enough

It is important, but it is not the premium design-result path.

## Practical Product Requirement

To align with this reference, our direct import should keep evolving from:

- parser

to:

- `Revit-side PT result package`

That package should include:

- 3D tendon model review
- profile drafting views
- sheet-ready package output
- takeoff output
- stable numbering
- later dimensioning and linked-tendon workflows

## Relationship To Other Benchmarks

Use this reference together with:

- `docs/PTBOTS_BENCHMARK_OVERVIEW.md`
- `docs/ADAPT_TWO_OPTION_IMPORT_STRATEGY.md`

Interpretation:

- `RISA / ADAPT workflow` helps define the return-to-Revit design handoff
- `PTBot workflow` helps define the inside-Revit automation layer after handoff

Together, they describe the strongest target state for `DRAWING PT`.
