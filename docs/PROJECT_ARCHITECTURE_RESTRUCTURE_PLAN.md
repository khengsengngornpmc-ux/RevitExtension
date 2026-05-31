# Project Architecture Restructure Plan

This document defines the target structure for the whole `RevitExtension` codebase.

The goal is to keep one strong Revit add-in while making it faster to extend, safer to maintain, and easier to test across multiple PCs.

## Current Implementation Snapshot

This plan is now partially implemented.

Feature folders already applied in the active project:

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

Supporting shared and technical foundation already applied:

- `Host/App`
- `Host/Commands`
- `Host/Shell`
- `Infrastructure/`
- `Infrastructure/Composition`
- `Infrastructure/RevitExecution`
- `Shared/Interop`
- `Shared/Revit`
- `Shared/Runtime`
- `Shared/Serialization`
- `Shared/Text`
- `Shared/UI`

Important note:

- `QSBoq` compile-time file ownership has already been updated into `Features/QSBoq`
- `CadToModel` compile-time file ownership has already been updated into `Features/CadToModel`
- `ArchitectureTools` compile-time file ownership has already been updated into `Features/ArchitectureTools`
- `PTDrawing`, `QSBoq`, `Rebar`, `BoredPile`, `PmDashboard`, `Linksheet`, and `Licensing` entry files are now staged under feature folders
- `SiteProgress` and `Borey` standalone runtime classes have been moved into feature folders
- the main shell has been moved under `Host/Shell`
- some large feature partials still duplicate logic inside `Host/Shell/CamboBIMWindow.xaml.cs` and need a second extraction pass before they should be added to the project file

## What We Have Now

The current codebase already contains strong business value:

- CAD to model workflows
- PT drawing import and sheet generation
- QS and BOQ tooling
- site progress
- borey tooling
- architecture tools
- licensing and diagnostics

The main problem is not missing capability.

The main problem is concentration:

- very large WPF window files
- very large external event handlers
- repeated file path and export logic in many features
- one flat compile list in the project file

That structure can keep working for small additions, but it will slow down a large multi-discipline extension.

## Architecture Decision

The best structure for this project is a `modular monolith`.

Why this is the best fit:

- Revit add-ins already run inside one host process
- Revit API work must stay controlled by `ExternalCommand`, `ExternalEvent`, and `Transaction` boundaries
- the team needs fast feature delivery more than distributed-service complexity
- PT, QS, architecture, and dashboards still need to share models, logging, licensing, and drawing helpers

This means:

- one deployable add-in
- many internal feature modules
- shared infrastructure with strict rules
- feature logic moved out of giant window and handler files over time

## Immediate Working Rule

Until the larger `src/` split happens, use this repo rule for all ongoing development:

- host shell files can remain at root
- feature-owned code should live under `Features/<FeatureName>/`
- cross-feature helpers should live under `Shared/`
- technical plumbing should live under `Infrastructure/`
- avoid adding new business logic directly into `Host/Shell/CamboBIMWindow.xaml.cs` unless it is only temporary shell glue

## Target Module Layout

Use this logical structure inside the current repository first:

```text
RevitExtension
|- App / Ribbon
|- Features
|  |- CadToModel
|  |- PTDrawing
|  |- QSBoq
|  |- SiteProgress
|  |- Borey
|  |- ArchitectureTools
|  |- PmDashboard
|  |- Linksheet
|  |- Diagnostics
|  |- Licensing
|- Infrastructure
|  |- Architecture
|  |- Diagnostics
|  |- Execution
|  |- Paths
|  |- Persistence
|  |- Security
|  |- UI
|- Shared
|  |- Contracts
|  |- Drafting
|  |- Geometry
|  |- Text
|  |- Export
|- docs
|- scripts
|- tools
```

## Feature Ownership Map

Use these module boundaries.

### `Features.CadToModel`

Move here over time:

- CAD source selection
- layer scanning
- element creation requests
- DWG and DXF preprocessing
- reusable import orchestration

Current prepared files:

- `Features/CadToModel/CadToModelExternalEventHandler.cs`
- `Features/CadToModel/CadToModelRequest.cs`
- `Features/CadToModel/Cad2ModelToolRegistry.cs`
- `Features/CadToModel/Cad2ModelPicker.cs`
- `Features/CadToModel/CadLayoutToColumnCommand.cs`
- `Features/CadToModel/OpenCad2ModelCommand.cs`
- `Features/CadToModel/MhnkCadToModelManagerWindow.cs`
- `Features/CadToModel/MhnkCadToModelPreviewWindow.cs`

### `Features.PTDrawing`

Move here over time:

- ADAPT direct import
- DWG and DXF PT fallback import
- tendon normalization
- tendon numbering and audit
- profile generation
- PT takeoff and PT sheets

Current hot files:

- `Features/PTDrawing/CamboBIMWindow.AdaptTendonImport.cs`
- PT sections inside `CadToModelExternalEventHandler.cs`
- `Features/PTDrawing/PtJsonModels.cs`
- `Features/PTDrawing/PtJsonMapper.cs`

### `Features.QSBoq`

Move here over time:

- measurement settings
- measurement rules
- QS exports
- BOQ outputs

Current hot files:

- `Features/QSBoq/CBIM_QS.cs`
- `Features/QSBoq/CBIM_BOQ.cs`
- `Features/QSBoq/QsMeasurementSettings.cs`
- `Features/QSBoq/QsMeasurementRules.cs`
- `Features/QSBoq/CamboBIMWindow.MeasurementSettings.cs`
- `Features/QSBoq/CamboBIMWindow.MeasurementRules.cs`
- `Features/QSBoq/CamboBIMWindow.TasExport.cs`

### `Features.Rebar`

Move here over time:

- column rebar generation
- beam rebar generation
- rebar preview and settings
- reinforcement drafting helpers

Current prepared files:

- `Features/Rebar/CamboBIMWindow.BeamNaviate.cs`
- `Features/Rebar/CamboBIMWindow.RebarColumn.cs`

### `Features.BoredPile`

Move here over time:

- pile CAD import picking
- layer filtering
- bored pile generation
- pile tool UI

Current prepared files:

- `Features/BoredPile/BoredPileToolExternalEventHandler.cs`
- `Features/BoredPile/BoredPileToolWindow.xaml`
- `Features/BoredPile/BoredPileToolWindow.xaml.cs`

### `Features.SiteProgress`

Move here over time:

- progress reporting
- dashboards
- report exports

Current hot files:

- `Features/SiteProgress/CBIM_SITE_PROGRESS.cs`
- `Features/SiteProgress/OpenSiteProgressCommand.cs`
- `CamboBIMWindow.SiteProgress.cs`

### `Features.Borey`

Move here over time:

- borey settings
- report generation
- borey exports

Current hot files:

- `Features/Borey/CBIM_BOREY.cs`
- `Features/Borey/OpenBoreyCommand.cs`
- `CamboBIMWindow.Borey.cs`

### `Features.ArchitectureTools`

Move here over time:

- ARC workspace logic
- previews
- validation
- smart mapping
- tool settings

Current prepared files:

- `Features/ArchitectureTools/OpenMhnkArchitectureToolCommand.cs`
- `Features/ArchitectureTools/MhnkArcCommandRuntime.cs`
- `Features/ArchitectureTools/MhnkArcPreviewService.cs`
- `Features/ArchitectureTools/MhnkArcToolsWindow.cs`
- `Features/ArchitectureTools/MhnkArcValidationWindow.cs`
- `Features/ArchitectureTools/MhnkSolidsInteractionWindow.cs`

### `Features.PmDashboard`

Move here over time:

- PM dashboard data transforms
- dashboard views
- dashboard exports

Current prepared files:

- `Features/PmDashboard/CamboBIMWindow.PmDashboard.cs`
- `Features/PmDashboard/OpenPmDashboardCommand.cs`
- `Features/PmDashboard/OpenSCurveCommand.cs`

### `Features.Linksheet`

Use as a future review platform for:

- PT audit and renumber grids
- quantity review tables
- drawing review status

Current prepared files:

- `Features/Linksheet/LinksheetModels.cs`
- `Features/Linksheet/OpenLinksheetCommand.cs`
- related window logic in `CamboBIMWindow.xaml.cs`

### `Features.Licensing`

Keep isolated:

- online license service
- login cache
- user authentication UI

Current files are grouped under `Features/Licensing`.

### `Features.Diagnostics`

Keep isolated:

- app diagnostics
- module inventory
- execution traces
- health reports

Current files:

- `Infrastructure\MhnkDiagnostics.cs`
- `Infrastructure\MhnkLogger.cs`

## UI Rules

The WPF shell should become a thin coordinator, not the business layer.

Target rules:

- `Host/Shell/CamboBIMWindow.xaml` stays as the main shell
- each tab or tool cluster gets its own partial file or view class
- button handlers should only collect input, call a workflow service, and show results
- heavy parsing, file IO, and drafting logic should never stay in the button click method

## Revit Execution Rules

These rules should stay strict across the whole project:

- only Revit-facing layers talk directly to `UIDocument`, `Document`, `Transaction`, `ElementId`, and selection APIs
- UI layers should prepare requests, not execute Revit mutations directly
- workflow services should build normalized request models before they reach Revit execution
- every large feature should have a request object, execution service, and result object

## Shared Infrastructure Rules

All features should reuse one shared foundation for:

- path resolution
- JSON persistence
- feature registry
- logging
- diagnostics
- text cleanup helpers
- report export helpers

Do not create new one-off folder conventions like:

- custom temp paths hidden in a feature file
- duplicated log file builders
- duplicated JSON read and write helpers

## Performance Rules

To keep the extension fast on large projects:

- scan CAD or Revit data once, then reuse normalized records
- separate collection, normalization, and drawing generation phases
- cache static config and mapping files
- avoid repeated `FilteredElementCollector` passes for the same result set
- isolate expensive view generation from lightweight review and audit steps
- write snapshots once and reuse them for PT audit, reports, and regeneration

## Stability Rules

To keep the extension stable across different PCs:

- centralize all feature paths under one root
- centralize export and snapshot contracts
- use structured diagnostics for every major workflow
- fail gracefully when optional files are missing
- keep direct Revit operations small and transactional

## Security Rules

To keep the extension safer:

- keep secrets and license data outside the git repo
- validate imported text and external file paths before use
- treat DWG, DXF, CSV, and external JSON as untrusted input
- write logs that help debugging without leaking secrets

## Migration Order

Use this order so delivery keeps moving while the structure improves.

### Stage 1. Freeze the foundation

Complete now:

- shared environment paths
- shared operation result model
- shared JSON file store
- shared feature catalog
- shared editor rules
- lightweight service registry and bootstrapper
- shared Revit `ExternalEvent` execution boundary
- shared Revit transaction runner

### Stage 1.1. Revit execution boundary

The next architecture layer has been added so future PT, CAD2MODEL, QS, ARC, and drawing tools can execute Revit API work through one controlled path:

- `Infrastructure/Composition/ExtensionServiceBootstrapper.cs`
- `Infrastructure/Composition/ExtensionServiceRegistry.cs`
- `Infrastructure/RevitExecution/IRevitExecutionRequest.cs`
- `Infrastructure/RevitExecution/RevitExecutionRequestBase.cs`
- `Infrastructure/RevitExecution/RevitExecutionContext.cs`
- `Infrastructure/RevitExecution/RevitExecutionQueue.cs`
- `Infrastructure/RevitExecution/RevitExecutionExternalEventHandler.cs`
- `Infrastructure/RevitExecution/RevitExecutionBoundary.cs`
- `Infrastructure/RevitExecution/RevitTransactionRunner.cs`

Feature migration rule:

- UI creates an `IRevitExecutionRequest`
- UI calls `RevitExecutionBoundary.Raise(request)`
- the shared `ExternalEvent` handler executes inside Revit's allowed API context
- feature request code uses `RevitTransactionRunner.Run(...)` for document changes
- each request writes feature trace logs through `FeatureTraceWriter`

### Stage 2. Extract PT workflow services

Extract from existing PT code:

- ADAPT file reading
- tendon normalization
- numbering rules
- profile drafting request building
- snapshot persistence

### Stage 3. Extract CAD and drafting services

Extract from `CadToModelExternalEventHandler.cs`:

- import preprocessors
- drafting text cleanup helpers
- sheet package builders
- view naming and metadata helpers

### Stage 4. Extract shell-level feature coordinators

Extract from `CamboBIMWindow.xaml.cs`:

- PT coordinator
- QS coordinator
- architecture tools coordinator
- site progress coordinator

### Stage 5. Split project files when the module seams are clean

After services stabilize, split into separate projects if needed:

- `CamboBIM.Core`
- `CamboBIM.Features`
- `CamboBIM.RevitHost`
- `CamboBIM.Tools`

Do not split early while giant files still hold mixed responsibilities.

## Immediate Code Priorities

The best next coding priorities after this foundation are:

1. move PT snapshot and trace writing onto shared infrastructure
2. extract PT import request models and PT result models
3. extract drafting text cleanup helpers out of `CadToModelExternalEventHandler.cs`
4. extract shell tab handlers out of `CamboBIMWindow.xaml.cs`

## Success Criteria

This restructure is successful when:

- PT, QS, architecture, and dashboard features all use the same infrastructure services
- new tools can be added without touching giant unrelated files
- logs, snapshots, and exports appear in predictable folders
- tomorrow's Revit-side validation can focus on behavior, not setup confusion
- the codebase keeps one deployment unit but behaves like a set of clean internal modules
