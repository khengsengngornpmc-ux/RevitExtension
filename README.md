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

Before pushing to a Revit test PC, run the version support verifier:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\verify-revit-version-support.ps1 -DesignTimeBuild
```

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
