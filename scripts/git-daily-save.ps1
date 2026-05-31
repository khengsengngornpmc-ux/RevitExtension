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
    throw "Current branch is '$currentBranch', but this save was asked to use '$Branch'. Switch branch or run with -Branch current."
  }

  git add -A

  git diff --cached --quiet
  $hasStagedChanges = ($LASTEXITCODE -ne 0)

  if (-not $hasStagedChanges) {
    Write-Host "No changes to commit."
    exit 0
  }

  if ([string]::IsNullOrWhiteSpace($Message)) {
    $timestamp = Get-Date -Format "yyyy-MM-dd HH:mm"
    $Message = "chore: daily save $timestamp"
  }

  git commit -m $Message

  if ($NoPush) {
    Write-Host "Commit created. Push skipped because -NoPush was provided."
    exit 0
  }

  git push origin $Branch
  Write-Host "Daily save complete on branch '$Branch'."
}
finally {
  Pop-Location
}
