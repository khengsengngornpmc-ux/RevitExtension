# RevitExtension

RevitExtension is the MHNK/CamboBIM Revit add-in source tree for CAD-to-model, QS measurement, rebar/formwork tooling, site progress, drawing automation, licensing, and related Revit workflows.

The whole-extension restructure target is documented in `docs/PROJECT_ARCHITECTURE_RESTRUCTURE_PLAN.md`.

## Verified Local Build

The locally verified target on this workstation is Revit 2025:

```powershell
dotnet build .\CamboBIM.Revit2025.Addin.csproj -c Debug -p:Platform=x64 -p:DisableRevitDeploy=true -v minimal
```

Revit API assemblies are resolved from the installed Revit path. Revit 2024 source is present, but this machine has only been verified for the Revit 2025 API.

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
