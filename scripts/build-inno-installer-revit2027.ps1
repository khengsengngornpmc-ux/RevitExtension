param(
    [string]$SourceDir = "",
    [switch]$IncludeAddinRuntime = $true,
    [string]$RuntimeDir = "",
    [string]$OutputDir = ".\release\inno",
    [string]$ScriptPath = ".\installer\CamboBIM.Revit2024.Deploy.iss",
    [string]$IsccPath = "",
    [switch]$AllowSmallRuntime
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$scriptDir = Split-Path -Parent $PSCommandPath
$params = @{
    RevitYear = "2027"
    SourceDir = $SourceDir
    IncludeAddinRuntime = $IncludeAddinRuntime
    RuntimeDir = $RuntimeDir
    OutputDir = $OutputDir
    ScriptPath = $ScriptPath
    IsccPath = $IsccPath
    AllowSmallRuntime = $AllowSmallRuntime
}

& (Join-Path $scriptDir "build-inno-installer.ps1") @params
