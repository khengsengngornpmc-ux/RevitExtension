param(
    [string]$OutputDir = ".\scripts"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$scriptDir = Split-Path -Parent $PSCommandPath
$years = @("2023", "2024", "2025", "2026", "2027")
$builder = Join-Path $scriptDir "build-deploy-exe.ps1"

if (-not (Test-Path -LiteralPath $builder)) {
    throw "Build script not found: $builder"
}

foreach ($year in $years) {
    $outputPath = Join-Path $OutputDir ("deploy-revit" + $year + "-addin.exe")
    Write-Host ""
    Write-Host "Building deploy EXE for Revit $year..."
    & $builder -RevitYear $year -OutputPath $outputPath
    if ($LASTEXITCODE -ne 0) {
        throw "Deploy EXE build failed for Revit $year with exit code $LASTEXITCODE."
    }
}

Write-Host ""
Write-Host "Deploy EXE build complete for Revit 2023-2027."
