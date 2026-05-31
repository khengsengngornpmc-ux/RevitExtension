param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Release",
    [ValidateSet("AnyCPU", "x64")]
    [string]$Platform = "x64",
    [string]$OutputDir = ".\release\inno",
    [string]$SourceDir = "",
    [string]$IsccPath = "",
    [switch]$BuildDeployExe = $true,
    [switch]$SkipMissingRuntime,
    [switch]$AllowSmallRuntime
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$scriptDir = Split-Path -Parent $PSCommandPath
$projectRoot = Split-Path -Parent $scriptDir
$years = @("2023", "2024", "2025", "2026", "2027")
$innoBuilder = Join-Path $scriptDir "build-inno-installer.ps1"
$deployExeBuilder = Join-Path $scriptDir "build-deploy-exe-all-revit.ps1"

if (-not (Test-Path -LiteralPath $innoBuilder)) {
    throw "Inno build script not found: $innoBuilder"
}

if ($BuildDeployExe) {
    if (-not (Test-Path -LiteralPath $deployExeBuilder)) {
        throw "Deploy EXE build-all script not found: $deployExeBuilder"
    }

    & $deployExeBuilder
    if ($LASTEXITCODE -ne 0) {
        throw "Deploy EXE build-all failed with exit code $LASTEXITCODE."
    }
}

$created = New-Object 'System.Collections.Generic.List[string]'
$skipped = New-Object 'System.Collections.Generic.List[string]'

foreach ($year in $years) {
    $runtimeCandidates = @(
        (Join-Path $projectRoot ("bin\Revit" + $year + "\" + $Platform + "\" + $Configuration)),
        (Join-Path $projectRoot ("bin\Revit" + $year + "\" + $Configuration))
    )
    $runtimeDir = $runtimeCandidates | Where-Object {
        Test-Path -LiteralPath (Join-Path $_ ("CamboBIM.Revit" + $year + ".Addin.dll"))
    } | Select-Object -First 1

    if ([string]::IsNullOrWhiteSpace($runtimeDir)) {
        $message = "Revit $year runtime missing for $Configuration|$Platform."
        if ($SkipMissingRuntime) {
            Write-Warning $message
            $skipped.Add($message) | Out-Null
            continue
        }

        $checked = ($runtimeCandidates | ForEach-Object { " - $_" }) -join [Environment]::NewLine
        throw "$message Paths checked:`n$checked"
    }

    Write-Host ""
    Write-Host "Building Inno installer for Revit $year..."

    $innoParams = @{
        RevitYear = $year
        RuntimeDir = $runtimeDir
        OutputDir = $OutputDir
    }

    if (-not [string]::IsNullOrWhiteSpace($SourceDir)) {
        $innoParams.SourceDir = $SourceDir
    }

    if (-not [string]::IsNullOrWhiteSpace($IsccPath)) {
        $innoParams.IsccPath = $IsccPath
    }

    if ($AllowSmallRuntime) {
        $innoParams.AllowSmallRuntime = $true
    }

    & $innoBuilder @innoParams
    if ($LASTEXITCODE -ne 0) {
        throw "Inno installer build failed for Revit $year with exit code $LASTEXITCODE."
    }

    $latest = Get-ChildItem -Path $OutputDir -Filter ("MHNK_RVT" + $year + "_EXTENSION_v1.00*.exe") -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTimeUtc -Descending |
        Select-Object -First 1
    if ($null -ne $latest) {
        $created.Add($latest.FullName) | Out-Null
    }
}

Write-Host ""
Write-Host "Inno installer build summary:"
foreach ($path in $created) {
    Write-Host "  created: $path"
}

foreach ($message in $skipped) {
    Write-Host "  skipped: $message"
}
