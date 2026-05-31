# Revit Version Support

## Support Matrix

| Revit year | Project | Target framework | Deploy wrapper |
| --- | --- | --- | --- |
| 2023 | `CamboBIM.Revit2023.Addin.csproj` | `net48` | `scripts\deploy-revit2023-addin.ps1` |
| 2024 | `CamboBIM.Revit2024.Addin.csproj` | `net48` | `scripts\deploy-revit2024-addin.ps1` |
| 2025 | `CamboBIM.Revit2025.Addin.csproj` | `net8.0-windows10.0.19041.0` | `scripts\deploy-revit2025-addin.ps1` |
| 2026 | `CamboBIM.Revit2026.Addin.csproj` | `net8.0-windows10.0.19041.0` | `scripts\deploy-revit2026-addin.ps1` |
| 2027 | `CamboBIM.Revit2027.Addin.csproj` | prepared `net10.0-windows10.0.19041.0` | `scripts\deploy-revit2027-addin.ps1` |

## Design Rule

All years currently share the source namespace `CamboBIM.Revit2024.Addin`. This keeps the large existing codebase stable while each host project produces a different DLL name, output folder, compile symbol, manifest template, and license product code.

Do not change manifest `FullClassName` values to year-specific namespaces until the full namespace migration is intentionally done across all `.cs`, `.xaml`, resource, and command references.

## Build Rule

Build only the Revit project that matches the installed Revit API on the machine. If Revit is not installed in the default Autodesk folder, pass `RevitInstallDir`:

```powershell
dotnet build .\CamboBIM.Revit2026.Addin.csproj -c Release -p:Platform=x64 -p:RevitInstallDir="D:\Apps\Autodesk\Revit 2026\"
```

Use `CamboBIM.AllRevitVersions.sln` for Visual Studio organization, but expect only projects with matching local Revit API assemblies to compile on that PC.

Run this before pushing to another PC:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\verify-revit-version-support.ps1 -DesignTimeBuild
```

## Reference Notes

Autodesk's Revit 2025 API migration notes state that Revit 2025 moved add-ins from .NET Framework 4.8 to .NET 8. Autodesk support material also describes Revit 2025/2026 API development on .NET 8. Keep the 2027 target as a prepared SDK line until it is validated against the installed Revit 2027 API assemblies on the test machine.

- https://blog.autodesk.io/migrating-from-net-48-to-net-core-8/
- https://www.autodesk.com/support/technical/article/caas/tsarticles/ts/1K0Qigx1sn8IJvUOO8Vzou.html
