Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$scriptDir = Split-Path -Parent $PSCommandPath
$targetScriptPath = Join-Path $scriptDir "reload-revit2024-addin.ps1"

if (-not (Test-Path -LiteralPath $targetScriptPath)) {
    throw "Target script not found: $targetScriptPath"
}

& $targetScriptPath -RevitYear 2027 @args
$exitCode = $LASTEXITCODE
if ($null -ne $exitCode) {
    exit $exitCode
}
