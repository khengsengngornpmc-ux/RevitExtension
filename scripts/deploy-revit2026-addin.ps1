Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$scriptDir = Split-Path -Parent $PSCommandPath
$targetScriptPath = Join-Path $scriptDir "deploy-revit2024-addin.ps1"

if (-not (Test-Path -LiteralPath $targetScriptPath)) {
    throw "Target script not found: $targetScriptPath"
}

& $targetScriptPath -RevitYear 2026 @args
$exitCodeVariable = Get-Variable -Name LASTEXITCODE -Scope Global -ErrorAction SilentlyContinue
if ($null -ne $exitCodeVariable -and $null -ne $exitCodeVariable.Value) {
    exit ([int]$exitCodeVariable.Value)
}
