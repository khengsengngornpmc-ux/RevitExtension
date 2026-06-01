# RevitExtension

RevitExtension is the MHNK/CamboBIM Revit add-in source tree for CAD-to-model, QS measurement, rebar/formwork tooling, site progress, drawing automation, licensing, and related Revit workflows.

The whole-extension restructure target is documented in `docs/PROJECT_ARCHITECTURE_RESTRUCTURE_PLAN.md`.

## Revit Version Support

This source tree is prepared as one shared codebase with per-Revit host projects:

- `CamboBIM.Revit2023.Addin.csproj` targets `net48`.
- `CamboBIM.Revit2024.Addin.csproj` targets `net48`.
- `CamboBIM.Revit2025.Addin.csproj` targets `net8.0-windows10.0.19041.0`.
- `CamboBIM.Revit2026.Addin.csproj` targets `net8.0-windows10.0.19041.0`.
- `CamboBIM.Revit2027.Addin.csproj` targets the prepared `net10.0-windows10.0.19041.0` line and must be validated against the installed Autodesk Revit 2027 SDK/API when available on the test PC.

Open `CamboBIM.AllRevitVersions.sln` in Visual Studio when you want to manage all five host projects together.

The shared source, WPF page, content, and resource registry lives in `CamboBIM.SharedProjectItems.targets`; each Revit-year project imports it so all host builds stay aligned.

The shared Revit execution boundary lives in `Infrastructure\RevitExecution`. New modeless UI workflows should create feature request objects and raise them through `RevitExecutionBoundary`, then use `RevitTransactionRunner` for document mutations.

External import paths and labels should go through `Infrastructure\Security\ExternalInputValidator` before reading ADAPT, DWG, DXF, Excel, CSV, JSON, or user-provided text.

PT import workflow decisions are being extracted into `Features\PTDrawing\PtDrawingImportWorkflowService.cs` so direct ADAPT/profile-table imports and DWG/DXF fallback imports use the same validation and path-normalization rules before reaching Revit.

PT import request filling is centralized in `Features\PTDrawing\PtDrawingCadToModelRequestBuilder.cs`, reducing duplicated handoff code before the Revit-side CAD2MODEL handler runs.

If Revit reports an external application load failure, run:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\diagnose-revit-addin-load.ps1 -RevitYear 2025
```

Deployment now refuses tiny design-time stub DLLs and requires `.deps.json` beside Revit 2025+ DLLs.
The deploy EXE is intentionally compiled as a console executable so automated scripts receive a real failure exit code.

Use `scripts\add-shared-project-item.ps1` when adding shared feature files, for example:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\add-shared-project-item.ps1 -ItemType Compile -Include Features\PTDrawing\NewService.cs
```

Before pushing to a Revit test PC, run the version support verifier:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\verify-revit-version-support.ps1 -DesignTimeBuild
```

## Deploy EXE and Inno Setup

Build the one-click deploy executables for every supported Revit version:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\build-deploy-exe-all-revit.ps1
```

After building a real `Release|x64` add-in DLL on a Revit API machine, create one Inno setup EXE per year:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\build-inno-installer-revit2023.ps1 -RuntimeDir .\bin\Revit2023\x64\Release
powershell -ExecutionPolicy Bypass -File .\scripts\build-inno-installer-revit2024.ps1 -RuntimeDir .\bin\Revit2024\x64\Release
powershell -ExecutionPolicy Bypass -File .\scripts\build-inno-installer-revit2025.ps1 -RuntimeDir .\bin\Revit2025\x64\Release
powershell -ExecutionPolicy Bypass -File .\scripts\build-inno-installer-revit2026.ps1 -RuntimeDir .\bin\Revit2026\x64\Release
powershell -ExecutionPolicy Bypass -File .\scripts\build-inno-installer-revit2027.ps1 -RuntimeDir .\bin\Revit2027\x64\Release
```

Output goes to `release\inno\MHNK_RVT<year>_EXTENSION_v1.00.exe`. Use `scripts\build-inno-installer-all-revit.ps1 -SkipMissingRuntime` on a build PC when you want to create every installer that has an available runtime folder.

## Verified Local Build

Build the project that matches the Revit version installed on the machine:

```powershell
dotnet build .\CamboBIM.Revit2023.Addin.csproj -c Debug -p:Platform=x64 -p:DisableRevitDeploy=true -v minimal
dotnet build .\CamboBIM.Revit2024.Addin.csproj -c Debug -p:Platform=x64 -p:DisableRevitDeploy=true -v minimal
dotnet build .\CamboBIM.Revit2025.Addin.csproj -c Debug -p:Platform=x64 -p:DisableRevitDeploy=true -v minimal
dotnet build .\CamboBIM.Revit2026.Addin.csproj -c Debug -p:Platform=x64 -p:DisableRevitDeploy=true -v minimal
dotnet build .\CamboBIM.Revit2027.Addin.csproj -c Debug -p:Platform=x64 -p:DisableRevitDeploy=true -v minimal
```

Revit API assemblies are resolved from the installed Revit path. If Revit is installed outside `C:\Program Files\Autodesk`, pass `-p:RevitInstallDir="D:\Apps\Autodesk\Revit 2026\"` with the matching year.

## Repository Scope

This public repository includes source code, add-in templates, scripts, documentation, icons, and configuration templates.

The repository intentionally excludes local build outputs, installer packages, generated diagnostics, local datasets, Power BI/database work files, nested old repository folders, and live license runtime configuration.

Use `license/online-license.config.json.template` or `license/online-license.google-sheet.sample.json` as examples. Do not commit a real `license/online-license.config.json`.

## Shop Drawing Automation

Recent work includes shop drawing automation for:

- selected-element plan, section, elevation, 3D, and dimension views
- formwork layout views and sheets
- rebar drawing views and sheets
- construction sheet/index generation
- title-block metadata and issue information
- site-ready selected-element detail panels with dimensions, element data, quantity summary, and readiness checks
