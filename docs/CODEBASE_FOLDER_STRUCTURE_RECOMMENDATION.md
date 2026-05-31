## Purpose

This document defines the recommended folder structure for the whole `RevitExtension` repository.

The goal is:

- keep the repo fast to navigate
- reduce the overloaded root folder
- align code ownership with features
- make future refactors predictable
- support one deployable Revit add-in with clean internal modules

This recommendation is based on the current repo state as of `2026-06-01`.

## Current Applied Structure

The repo is not fully at the final `src/` layout yet, but the feature-folder direction is now active and should be treated as the standard pattern.

Current feature folders already in use:

- `Features/PTDrawing`
- `Features/CadToModel`
- `Features/ArchitectureTools`
- `Features/QSBoq`
- `Features/Rebar`
- `Features/BoredPile`
- `Features/PmDashboard`
- `Features/Linksheet`
- `Features/Licensing`
- `Features/SiteProgress`
- `Features/Borey`

Current host and shared folders already in use:

- `Host/App`
- `Host/Commands`
- `Host/Shell`
- `Shared/Interop`
- `Shared/Revit`
- `Shared/Runtime`
- `Shared/Serialization`
- `Shared/Text`
- `Shared/UI`

This means new feature-owned code should stop going into the repo root whenever there is a clear owning feature folder already available.

For this stage, the practical rule is:

- keep the existing single-project add-in
- move feature-owned files into `Features/<FeatureName>/`
- keep `Infrastructure/` for technical foundation
- keep `Shared/` for cross-feature reusable helpers
- only introduce `src/` later when feature ownership is stable

## Main Problems In The Current Layout

The project already has good value, but the folder organization is still messy for a large extension:

- too many `.cs` files live directly in the repo root
- feature code is split between root files, partial window files, and a few feature folders
- shell/UI files and business logic still mix together
- some feature state/models are duplicated between `CamboBIMWindow.xaml.cs` and feature partials
- images, config, deploy, and build assets are better grouped than feature code

The biggest organization issue is not that folders are missing.

The biggest issue is inconsistent ownership.

The PT drawing folder is currently the best reference implementation for how feature code should be grouped.

## Recommended Top-Level Structure

Use this as the target root layout:

```text
RevitExtension
|- .github/
|- addins/
|- build/
|  |- deploy/
|  |- installer/
|- config/
|- docs/
|- scripts/
|- src/
|  |- Host/
|  |- Features/
|  |- Infrastructure/
|  |- Shared/
|  |- Assets/
|- tests/
|- tools/
|- Directory.Build.props
|- .editorconfig
|- .gitattributes
|- .gitignore
|- README.md
```

## What Each Top-Level Folder Should Own

### `src/Host`

Everything required to boot the Revit add-in and present the shell:

- `App.cs`
- `CamboBIMWindow.xaml`
- shell-only partials
- ribbon/open commands
- external command entry points

Suggested subfolders:

```text
src/Host
|- App/
|- Commands/
|- Shell/
|  |- CamboBIMWindow.xaml
|  |- CamboBIMWindow.xaml.cs
|  |- CamboBIMWindowCommand.cs
|- RevitHost/
```

### `src/Features`

Feature-owned code only.

Each feature gets one folder and keeps its:

- workflows
- requests/results
- feature UI partials
- parsing
- exports
- feature-specific models

Suggested structure:

```text
src/Features
|- CadToModel/
|- PTDrawing/
|- QSBoq/
|- SiteProgress/
|- Borey/
|- ArchitectureTools/
|- PmDashboard/
|- Linksheet/
|- BoredPile/
|- Licensing/
```

### `src/Infrastructure`

Cross-feature technical foundation:

- diagnostics
- environment paths
- persistence
- feature registry
- execution helpers

Keep this infrastructure-focused and not business-feature-focused.

### `src/Shared`

Pure reusable code shared across many features:

- text normalization
- geometry helpers
- Excel/CSV helper models
- COM/Revit-safe wrappers
- export helpers
- shared contracts

Suggested subfolders:

```text
src/Shared
|- Contracts/
|- Text/
|- Geometry/
|- Interop/
|- Export/
|- UI/
|- Revit/
```

### `src/Assets`

Move repo assets under one place:

```text
src/Assets
|- Images/
|- Icons/
|- Templates/
|- GuideAssets/
```

This is cleaner than keeping `images/` at root long term.

### `build`

Recommended final ownership:

```text
build
|- addins/
|- deploy/
|- installer/
```

This should absorb the current `addins/`, `deploy/`, and `installer/` folders over time.

### `config`

For non-code config tracked in git:

- tool registries
- feature defaults
- import mappings

This should absorb the current `Config/` folder over time.

### `tests`

Even if tests start small, reserve the folder now.

Suggested future layout:

```text
tests
|- Unit/
|- Integration/
|- GoldenFiles/
```

## Recommended Feature Folder Layout

Use the same internal pattern in every feature folder:

```text
FeatureName
|- Commands/
|- UI/
|- Workflows/
|- Services/
|- Models/
|- Persistence/
|- Imports/
|- Exports/
|- Diagnostics/
```

Not every feature needs every subfolder, but the pattern should stay consistent.

For the current repo size, start simple and only add subfolders when the feature genuinely needs them.

Good first step inside a feature folder:

```text
FeatureName
|- CamboBIMWindow.Feature.cs
|- FeatureRuntime.cs
|- FeatureModels.cs
|- FeatureSettings.cs
|- FeatureServices.cs
```

Then grow into `UI/`, `Models/`, `Services/`, `Imports/`, `Exports/`, or `Diagnostics/` as the feature becomes denser.

## Recommended Mapping From Current Files

### `CadToModel`

Move these toward:

```text
src/Features/CadToModel
|- Commands/
|- UI/
|- Services/
|- Models/
|- Imports/
```

Files now prepared under `Features/CadToModel`:

- `CadToModelExternalEventHandler.cs`
- `CadToModelRequest.cs`
- `Cad2ModelToolRegistry.cs`
- `Cad2ModelPicker.cs`
- `Cad2ModelPicker.xaml.xml`
- `CadLayoutToColumnCommand.cs`
- `OpenCad2ModelCommand.cs`
- `MhnkCadToModelManagerWindow.cs`
- `MhnkCadToModelPreviewWindow.cs`

Root files that still need a later extraction pass:

- `CamboBIMWindow.RebarColumn.cs`
- `CamboBIMWindow.BeamNaviate.cs`

### `PTDrawing`

This feature already started moving in the right direction.

Target:

```text
src/Features/PTDrawing
|- UI/
|- Imports/
|- Parsing/
|- Drafting/
|- Persistence/
|- Diagnostics/
|- Models/
```

Current files that belong here:

- `Features/PTDrawing/CamboBIMWindow.AdaptTendonImport.cs`
- `Features/PTDrawing/PtJsonModels.cs`
- `Features/PTDrawing/PtJsonMapper.cs`
- `Features/PTDrawing/OpenDrawingCommand.cs`
- `Features/PTDrawing/OpenDrawingPtCommand.cs`
- existing `Features/PTDrawing/*`

### `QSBoq`

Target:

```text
src/Features/QSBoq
|- UI/
|- Workflows/
|- Reports/
|- Exports/
|- Models/
|- Rules/
```

Current files that belong here:

- `Features/QSBoq/CBIM_QS.cs`
- `Features/QSBoq/CBIM_BOQ.cs`
- `Features/QSBoq/QsMeasurementSettings.cs`
- `Features/QSBoq/QsMeasurementRules.cs`
- `Features/QSBoq/CamboBIMWindow.TasExport.cs`
- `Features/QSBoq/TasReferenceSettings.cs`
- `Features/QSBoq/TasIdentificationOptions.cs`
- `Features/QSBoq/OpenQsToolCommand.cs`
- `CamboBIMWindow.Boq.cs`
- `CamboBIMWindow.MeasurementSettings.cs`
- `CamboBIMWindow.MeasurementRules.cs`
- `CamboBIMWindow.MeasurementRulesSync.cs`

### `Rebar`

Target:

```text
src/Features/Rebar
|- UI/
|- Workflows/
|- Models/
|- Preview/
|- Revit/
```

Files now prepared under `Features/Rebar`:

- `CamboBIMWindow.BeamNaviate.cs`
- `CamboBIMWindow.RebarColumn.cs`

### `SiteProgress`

Target:

```text
src/Features/SiteProgress
|- UI/
|- Workflows/
|- Reports/
|- Exports/
|- Models/
```

Current files that belong here:

- `Features/SiteProgress/CBIM_SITE_PROGRESS.cs`
- `Features/SiteProgress/OpenSiteProgressCommand.cs`
- `CamboBIMWindow.SiteProgress.cs`

### `Borey`

Target:

```text
src/Features/Borey
|- UI/
|- Data/
|- Reports/
|- Imports/
|- Exports/
```

Current files that belong here:

- `Features/Borey/CBIM_BOREY.cs`
- `Features/Borey/OpenBoreyCommand.cs`
- `CamboBIMWindow.Borey.cs`

### `ArchitectureTools`

Target:

```text
src/Features/ArchitectureTools
|- UI/
|- Workflows/
|- Models/
|- Validation/
|- Preview/
```

Files now prepared under `Features/ArchitectureTools`:

- `OpenMhnkArchitectureToolCommand.cs`
- `MhnkArcCommandRuntime.cs`
- `MhnkArcGuideline.cs`
- `MhnkArcPreviewModels.cs`
- `MhnkArcPreviewService.cs`
- `MhnkArcQaDashboardWindow.cs`
- `MhnkArcSmartMappingWindow.cs`
- `MhnkArcToolMetadata.cs`
- `MhnkArcToolSettingsWindow.cs`
- `MhnkArcToolsWindow.cs`
- `MhnkArcValidationWindow.cs`
- `MhnkRoomCreationOptionsWindow.cs`
- `MhnkSolidsInteractionWindow.cs`
- `MhnkUiTheme.cs`

### `PmDashboard`

Target:

```text
src/Features/PmDashboard
|- UI/
|- Workflows/
|- Reports/
|- Integrations/
|- Models/
```

Files now prepared under `Features/PmDashboard`:

- `CamboBIMWindow.PmDashboard.cs`
- `OpenPmDashboardCommand.cs`
- `OpenSCurveCommand.cs`

### `Linksheet`

Target:

```text
src/Features/Linksheet
|- UI/
|- Models/
|- Services/
```

Files now prepared under `Features/Linksheet`:

- `LinksheetModels.cs`
- `OpenLinksheetCommand.cs`

### `BoredPile`

Target:

```text
src/Features/BoredPile
|- UI/
|- Commands/
|- Services/
```

Files now prepared under `Features/BoredPile`:

- `BoredPileToolExternalEventHandler.cs`
- `BoredPileToolWindow.xaml`
- `BoredPileToolWindow.xaml.cs`

### `Licensing`

Current licensing source is grouped under `Features/Licensing`.

The root `license/` folder remains for deployable configuration templates and samples.

```text
Features/Licensing
```

## Recommended Shell Structure

The shell is now physically grouped under `Host/Shell` and should become much thinner:

```text
Host/Shell
|- CamboBIMWindow.xaml
|- CamboBIMWindow.xaml.cs
|- CamboBIMWindowCommand.cs
|- CamboBIMWindow.DrawingShell.cs
|- CamboBIMWindow.NavigationShell.cs
|- CamboBIMWindow.StatusShell.cs
```

Rule:

- shell files should only coordinate UI events
- feature logic must live in `src/Features/*`
- request preparation can stay close to UI
- business logic should leave the shell

## Recommended Shared Structure

The shared folder now has a real cross-feature structure.

Current shared structure:

```text
Shared
|- Text/
|  |- ExtensionTextUtility.cs
|- Interop/
|  |- ComActiveObject.cs
|- Revit/
|  |- SharedParameterPathResolver.cs
|- Runtime/
|  |- CamboBimRuntime.cs
|- Serialization/
|  |- CamboBimJson.cs
|- UI/
|  |- IdentifyElementScheduleWindow.cs
|  |- IdentifySvgIconFactory.cs
|  |- ProgressDialogWindow.cs
```

Move likely candidates here over time:

- small cross-feature formatting helpers
- generic Excel export helpers

## Recommended Build/Operations Structure

Current operations files are useful, but the folder names can become clearer:

```text
build/
|- deploy/
|- installer/
|- manifests/

scripts/
|- dev/
|- ci/
|- release/
|- git/
```

Files now moved under `scripts/`:

- `Run-Git-Daily-Save-RevitExtension.cmd`
- `Run-Git-Daily-Save-RevitExtension-PT.cmd`
- `push-to-github.cmd`

Move likely files here over time:

- current `Operation Local and Online/*`

## Recommended Naming Rules

To keep the structure clean:

- do not add new feature `.cs` files to the repo root
- new feature UI partials go inside the feature folder, not beside `CamboBIMWindow.xaml.cs`
- new commands go into `Host/Commands` or `Features/<Feature>/Commands`
- use one ownership folder for each model type
- keep root only for repo-level files and temporary migration leftovers

## What The Root Folder Should Contain After Cleanup

Target end state for the repo root:

```text
RevitExtension
|- .github/
|- addins/
|- build/
|- config/
|- docs/
|- scripts/
|- src/
|- tests/
|- tools/
|- .editorconfig
|- .gitattributes
|- .gitignore
|- Directory.Build.props
|- README.md
|- CamboBIM.Revit2024.Addin.sln
|- CamboBIM.Revit2025.Addin.sln
```

Everything else should move under `src/` or `build/`.

## Best Migration Order

Do not move everything at once.

Use this order:

1. keep extracting code ownership into feature partials and folders without changing namespaces
2. stop adding new root-level feature files
3. move new shared helpers into `Shared/*`
4. finish consolidating duplicate shell members into feature partials
5. create missing feature folders under `Features/`
6. move root feature files into those folders and update the `.csproj`
7. move shell-only code into `Host/Shell`
8. move deploy/installer/config assets into their final root folders
9. only after the seams are stable, consider a multi-project split

## Best Recommendation Right Now

The best design choice for this repository is:

- keep one add-in project for now
- organize code as if `src/Features/*` already exists
- treat `CamboBIMWindow.xaml.cs` and `CadToModelExternalEventHandler.cs` as temporary migration shells
- keep removing duplicated feature ownership from the root and from giant shell files

This gives the fastest path to:

- better performance for the team
- less confusion in the root folder
- safer future refactors
- clearer feature ownership across the whole extension
