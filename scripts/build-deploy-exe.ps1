param(
    [string]$OutputPath = "",
    [ValidatePattern("^\d{4}$")]
    [string]$RevitYear = "2024"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$projectRoot = Split-Path -Parent $PSScriptRoot
$sourcePath = Join-Path $projectRoot "tools\DeployRevitAddinExe\Program.cs"

if (-not (Test-Path -LiteralPath $sourcePath)) {
    throw "Source file not found: $sourcePath"
}

$resolvedRoot = Resolve-Path -LiteralPath "." | Select-Object -ExpandProperty Path
if ([string]::IsNullOrWhiteSpace($OutputPath)) {
    $OutputPath = ".\scripts\deploy-revit" + $RevitYear + "-addin.exe"
}
$targetPath = if ([System.IO.Path]::IsPathRooted($OutputPath)) {
    $OutputPath
} else {
    [System.IO.Path]::GetFullPath((Join-Path $resolvedRoot $OutputPath))
}

$targetDir = Split-Path -Parent $targetPath
if (-not (Test-Path -LiteralPath $targetDir)) {
    New-Item -Path $targetDir -ItemType Directory | Out-Null
}

$cscCandidates = @(
    "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe",
    "C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe"
)

$cscPath = $cscCandidates | Where-Object { Test-Path -LiteralPath $_ } | Select-Object -First 1
if ([string]::IsNullOrWhiteSpace($cscPath)) {
    throw "csc.exe not found. Install .NET Framework build tools."
}

Write-Host "Compiling deploy EXE with:"
Write-Host "  $cscPath"
Write-Host "Source:"
Write-Host "  $sourcePath"
Write-Host "Output:"
Write-Host "  $targetPath"

& $cscPath `
    /nologo `
    /target:exe `
    /platform:anycpu `
    /optimize+ `
    /reference:System.Windows.Forms.dll `
    /reference:System.Drawing.dll `
    /out:$targetPath `
    $sourcePath
if ($LASTEXITCODE -ne 0) {
    throw "Compilation failed with exit code $LASTEXITCODE."
}

Write-Host ""
Write-Host "Deploy EXE created:"
Write-Host "  $targetPath"
