param(
  [string]$Message = "",
  [string]$Branch = "",
  [switch]$NoPush
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$repoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
Push-Location $repoRoot

try {
  $currentBranch = (git branch --show-current).Trim()
  if ([string]::IsNullOrWhiteSpace($currentBranch)) {
    throw "Could not detect current Git branch."
  }

  if ([string]::IsNullOrWhiteSpace($Branch) -or $Branch -in @("current", ".")) {
    $Branch = $currentBranch
  }

  if ($currentBranch -ne $Branch) {
    throw "Current branch is '$currentBranch', but this PT save was asked to use '$Branch'. Switch branch or run with -Branch current."
  }

  $ptPaths = @(
    "CamboBIM.AllRevitVersions.sln",
    "CamboBIM.Revit2023.Addin.csproj",
    "CamboBIM.Revit2024.Addin.csproj",
    "CamboBIM.Revit2025.Addin.csproj",
    "CamboBIM.Revit2026.Addin.csproj",
    "CamboBIM.Revit2027.Addin.csproj",
    "CamboBIM.SharedProjectItems.targets",
    "Directory.Build.props",
    "Features/CadToModel/CadToModelExternalEventHandler.cs",
    "Features/CadToModel/CadToModelRequest.cs",
    "Features/PTDrawing/CamboBIMWindow.AdaptTendonImport.cs",
    "Features/PTDrawing/PtDrawingCadToModelRequestBuilder.cs",
    "Features/PTDrawing/PtDrawingAdmParserModels.cs",
    "Features/PTDrawing/PtDrawingAdmReaderService.cs",
    "Features/PTDrawing/PtDrawingDraftingAnnotationService.cs",
    "Features/PTDrawing/PtDrawingImportWorkflowModels.cs",
    "Features/PTDrawing/PtDrawingImportWorkflowService.cs",
    "Features/PTDrawing/PtDrawingTableParserModels.cs",
    "Features/PTDrawing/PtDrawingTableReaderService.cs",
    "Features/PTDrawing/PtDrawingTableRowParserService.cs",
    "Features/PTDrawing/PtJsonMapper.cs",
    "Features/PTDrawing/PtJsonModels.cs",
    "Host/App/App.cs",
    "Host/Commands/HelloCommand.cs",
    "Infrastructure/Composition/ExtensionServiceBootstrapper.cs",
    "Infrastructure/Composition/ExtensionServiceRegistry.cs",
    "Infrastructure/MhnkDiagnostics.cs",
    "Infrastructure/RevitExecution/IRevitExecutionRequest.cs",
    "Infrastructure/RevitExecution/RevitExecutionBoundary.cs",
    "Infrastructure/RevitExecution/RevitExecutionContext.cs",
    "Infrastructure/RevitExecution/RevitExecutionExternalEventHandler.cs",
    "Infrastructure/RevitExecution/RevitExecutionQueue.cs",
    "Infrastructure/RevitExecution/RevitExecutionRequestBase.cs",
    "Infrastructure/RevitExecution/RevitTransactionRunner.cs",
    "Infrastructure/Security/ExternalInputValidator.cs",
    "Properties/AssemblyInfo.cs",
    "Properties/launchSettings.json",
    "Properties/Revit2023MissingApiDesignTimeStub.cs",
    "Properties/Revit2024MissingApiDesignTimeStub.cs",
    "Properties/Revit2025MissingApiDesignTimeStub.cs",
    "Properties/Revit2026MissingApiDesignTimeStub.cs",
    "Properties/Revit2027MissingApiDesignTimeStub.cs",
    "README.md",
    "Shared/Runtime/CamboBimRuntime.cs",
    "addins/CamboBIM.Revit2023.Addin.addin.template",
    "addins/CamboBIM.Revit2024.Addin.addin.template",
    "addins/CamboBIM.Revit2025.Addin.addin.template",
    "addins/CamboBIM.Revit2026.Addin.addin.template",
    "addins/CamboBIM.Revit2027.Addin.addin.template",
    "scripts/Run-Git-Daily-Save-RevitExtension-PT.cmd",
    "scripts/deploy-revit2023-addin.ps1",
    "scripts/deploy-revit2024-addin.ps1",
    "scripts/deploy-revit2025-addin.ps1",
    "scripts/deploy-revit2026-addin.ps1",
    "scripts/deploy-revit2027-addin.ps1",
    "scripts/reload-revit2023-addin.ps1",
    "scripts/reload-revit2024-addin.ps1",
    "scripts/reload-revit2025-addin.ps1",
    "scripts/reload-revit2026-addin.ps1",
    "scripts/reload-revit2027-addin.ps1",
    "scripts/add-shared-project-item.ps1",
    "scripts/build-deploy-exe-all-revit.ps1",
    "scripts/build-inno-installer.ps1",
    "scripts/build-inno-installer-all-revit.ps1",
    "scripts/build-inno-installer-revit2023.ps1",
    "scripts/build-inno-installer-revit2024.ps1",
    "scripts/build-inno-installer-revit2025.ps1",
    "scripts/build-inno-installer-revit2026.ps1",
    "scripts/build-inno-installer-revit2027.ps1",
    "scripts/verify-revit-version-support.ps1",
    "installer/CamboBIM.Revit2024.Deploy.iss",
    "installer/assets/CamboBIM_Logo_55.bmp",
    "installer/assets/CamboBIM_Logo.ico",
    "installer/assets/MHNK_Logo_55.bmp",
    "installer/assets/MHNK_Logo.ico",
    "Operation Local and Online/02-Step2-PT-Only-Save.cmd",
    "scripts/git-daily-save-pt.ps1",
    "docs/ADAPT_BUILDER_IMPORT_GUIDE.md",
    "docs/ADAPT_DIRECT_IMPORT_ARCHITECTURE.md",
    "docs/ADAPT_PROFILE_VISUAL_ROADMAP.md",
    "docs/ADAPT_TWO_OPTION_IMPORT_STRATEGY.md",
    "docs/PT_COMBINED_EXTENSION_INTEGRATION_PLAN.md",
    "docs/PT_JSON_SCHEMA.md",
    "docs/PT_JSON_SCHEMA_EXAMPLE.json",
    "docs/PT_REVIT_TEST_CHECKLIST.md",
    "docs/PTBOT_ALIGN_TENDON_BUBBLES_WORKFLOW.md",
    "docs/PTBOT_CREATE_3D_VIEW_WORKFLOW.md",
    "docs/PTBOT_DEMO_SHOP_DRAWINGS_IN_MINUTES_WORKFLOW.md",
    "docs/PTBOT_DIMENSION_TENDONS_WORKFLOW.md",
    "docs/PTBOT_LINK_TENDONS_WORKFLOW.md",
    "docs/PTBOT_RAM_CONCEPT_IMPORT_WORKFLOW.md",
    "docs/PTBOT_RESOLVE_CHAIR_CLASHES_WORKFLOW.md",
    "docs/PTBOT_SHOW_HIDE_INTERMEDIATE_CHAIRS_WORKFLOW.md",
    "docs/PTBOTS_BENCHMARK_OVERVIEW.md",
    "docs/PTBOTS_CHANNEL_ALIGNMENT_MATRIX.md",
    "docs/DEV_SETUP.md",
    "docs/REVIT_VERSION_SUPPORT.md",
    "docs/RISA_ADAPT_REVIT_RETURN_WORKFLOW.md"
  )

  $stageablePaths = @($ptPaths | Where-Object { Test-Path $_ })
  if ($stageablePaths.Count -eq 0) {
    throw "No PT paths were found to stage."
  }

  & git add -A -- @stageablePaths

  git diff --cached --quiet
  $hasStagedChanges = ($LASTEXITCODE -ne 0)

  if (-not $hasStagedChanges) {
    Write-Host "No PT changes to commit."
    exit 0
  }

  if ([string]::IsNullOrWhiteSpace($Message)) {
    $timestamp = Get-Date -Format "yyyy-MM-dd HH:mm"
    $Message = "chore: PT daily save $timestamp"
  }

  git commit -m $Message

  if ($NoPush) {
    Write-Host "PT commit created. Push skipped because -NoPush was provided."
    exit 0
  }

  git push origin $Branch
  Write-Host "PT daily save complete on branch '$Branch'."
}
finally {
  Pop-Location
}
