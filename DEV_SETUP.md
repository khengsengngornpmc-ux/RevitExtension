# CamboBIM Revit 2024/2025 - Dev Setup

## 1) Requirements
- Revit 2024 installed
- .NET Framework 4.8 targeting pack
- For Revit 2025: Revit 2025 installed, .NET 8 SDK, and Visual Studio/Build Tools with the .NET desktop workload
- Visual Studio with Desktop development workload (or MSBuild tools)

## 2) Revit API reference path
The project now uses `$(RevitInstallDir)` for `RevitAPI.dll` and `RevitAPIUI.dll`.

Default fallback:
- `C:\Program Files\Autodesk\Revit 2024\`
- `C:\Program Files\Autodesk\Revit 2025\`

If Revit is installed elsewhere, build with:

```powershell
dotnet build CamboBIM.Revit2024.Addin.csproj -p:RevitInstallDir="D:\Apps\Autodesk\Revit 2024\"
dotnet build CamboBIM.Revit2025.Addin.csproj -p:RevitInstallDir="D:\Apps\Autodesk\Revit 2025\"
```

## 3) Debug settings (per machine)
Update `CamboBIM.Revit2024.Addin.csproj.user` if needed:
- `StartProgram` -> your local `Revit.exe`
- `StartWorkingDirectory` -> Revit install folder

Example:
- `C:\Program Files\Autodesk\Revit 2024\Revit.exe`

## 4) Build
Preferred (full .NET Framework MSBuild from Visual Studio):

```powershell
"C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" CamboBIM.Revit2024.Addin.csproj /t:Build /p:Configuration=Debug /v:minimal
"C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe" CamboBIM.Revit2025.Addin.csproj /restore /t:Build /p:Configuration=Release /p:Platform=x64 /v:minimal
```

Alternative (if your machine has compatible WPF build tasks configured):

```powershell
dotnet build CamboBIM.Revit2024.Addin.csproj -v minimal
dotnet build CamboBIM.Revit2025.Addin.csproj -c Release -p:Platform=x64 -v minimal
```

## 5) Register add-in in Revit (required on each PC)
After build, run:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\deploy-revit2024-addin.ps1 -Configuration Debug -Platform AnyCPU -RevitYear 2024
```

If you built `Release|x64` in Visual Studio:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\deploy-revit2024-addin.ps1 -Configuration Release -Platform x64 -RevitYear 2024
```

This creates:
- `%AppData%\Autodesk\Revit\Addins\2024\CamboBIM.Revit2024.Addin.addin`
- If duplicate CamboBIM manifests exist in `%ProgramData%\Autodesk\Revit\Addins\2024`, the script disables them automatically.
  - Note: disabling files under `%ProgramData%` may require running PowerShell as Administrator.

Then restart Revit. You should see the `CamboBIM` tab.

### Revit 2025 manifest deploy
After building the Revit 2025 project, run:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\deploy-revit2025-addin.ps1 -Configuration Release -Platform x64
```

This creates:
- `%AppData%\Autodesk\Revit\Addins\2025\CamboBIM.Revit2025.Addin.addin`
- A shadow copy under `%LocalAppData%\CamboBIM\Revit\Addins\2025\dev`

The Revit 2025 DLL is:
- `.\bin\Revit2025\x64\Release\CamboBIM.Revit2025.Addin.dll`

## 5.1) Deploy using EXE (one-click)
Build the deploy executable:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\build-deploy-exe.ps1
powershell -ExecutionPolicy Bypass -File .\scripts\build-deploy-exe.ps1 -RevitYear 2025
```

Run the deploy executable:

```powershell
.\scripts\deploy-revit2024-addin.exe --configuration Debug --platform AnyCPU --revit-year 2024
.\scripts\deploy-revit2025-addin.exe --configuration Release --platform x64 --revit-year 2025
```

If you need admin rights to disable duplicate manifests in `%ProgramData%`, run the EXE as Administrator.

## 5.2) Build installer with Inno Setup
If you want a Setup installer (`Setup.exe`) for distribution:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\build-inno-installer.ps1
powershell -ExecutionPolicy Bypass -File .\scripts\build-inno-installer.ps1 -RevitYear 2025 -RuntimeDir .\bin\Revit2025\x64\Release
```

This uses the latest folder under:
- `.\release\CamboBIM_Deploy_Package_*`
and merges add-in runtime files from:
- `.\bin\Debug\` (contains `CamboBIM.Revit2024.Addin.dll`)

Output:
- `.\release\inno\CamboBIM_RVT2024_EXTENSION_v1.00.exe`
- `.\release\inno\CamboBIM_RVT2025_EXTENSION_v1.00.exe`

## 6) Reload during development (recommended)
Revit application add-ins do not hot-reload while Revit is running.  
Use this script to close Revit, build, deploy, and start Revit again in one command:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\reload-revit2024-addin.ps1 -Configuration Debug -Platform AnyCPU -RevitYear 2024
powershell -ExecutionPolicy Bypass -File .\scripts\reload-revit2025-addin.ps1 -Configuration Release -Platform x64
```

If Revit does not close by itself:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\reload-revit2024-addin.ps1 -Configuration Debug -Platform AnyCPU -RevitYear 2024 -ForceClose
powershell -ExecutionPolicy Bypass -File .\scripts\reload-revit2025-addin.ps1 -Configuration Release -Platform x64 -ForceClose
```

## 7) Online license (optional)
To enable online login + seat management:

1. Copy `license\online-license.config.json.template` to one of:
   - `%ProgramData%\CamboBIM\online-license.config.json` (recommended)
   - `<addin folder>\online-license.config.json`
   - `<addin folder>\license\online-license.config.json`
2. Set `"enabled": true` and configure your server URL/endpoints.
3. See API contract: `docs\ONLINE_LICENSE_API.md`

If you only have Google Sheet (no private server):

1. Use Apps Script backend file: `docs\GOOGLE_APPS_SCRIPT_LICENSE.gs`
2. Follow setup steps: `docs\GOOGLE_SHEET_LICENSE_SETUP.md`
3. Start from sample config: `license\online-license.google-sheet.sample.json`
