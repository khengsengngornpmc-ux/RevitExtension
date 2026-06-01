param(
    [ValidatePattern("^\d{4}$")]
    [string]$RevitYear = "2025",
    [string]$AddinName = ""
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($AddinName)) {
    $AddinName = "CamboBIM.Revit" + $RevitYear + ".Addin"
}

$manifestFileName = $AddinName + ".addin"
$manifestPaths = @(
    (Join-Path $env:APPDATA ("Autodesk\Revit\Addins\" + $RevitYear + "\" + $manifestFileName)),
    (Join-Path $env:ProgramData ("Autodesk\Revit\Addins\" + $RevitYear + "\" + $manifestFileName))
) | Select-Object -Unique

Write-Host "MHNK Revit add-in load diagnosis"
Write-Host "RevitYear: $RevitYear"
Write-Host "Manifest name: $manifestFileName"
Write-Host ""

foreach ($manifestPath in $manifestPaths) {
    Write-Host "Manifest:"
    Write-Host "  $manifestPath"

    if (-not (Test-Path -LiteralPath $manifestPath)) {
        Write-Host "  Status: missing"
        Write-Host ""
        continue
    }

    Write-Host "  Status: found"
    [xml]$xml = Get-Content -LiteralPath $manifestPath
    $node = $xml.SelectSingleNode("//AddIn")
    $assemblyPath = ""
    $fullClassName = ""
    $addinId = ""

    if ($null -ne $node) {
        $assemblyPath = if ($null -eq $node.Assembly) { "" } else { [string]$node.Assembly.InnerText }
        $fullClassName = if ($null -eq $node.FullClassName) { "" } else { [string]$node.FullClassName.InnerText }
        $addinId = if ($null -eq $node.AddInId) { "" } else { [string]$node.AddInId.InnerText }
    }

    Write-Host "  AddInId: $addinId"
    Write-Host "  FullClassName: $fullClassName"
    Write-Host "  Assembly: $assemblyPath"

    if ([string]::IsNullOrWhiteSpace($assemblyPath) -or -not (Test-Path -LiteralPath $assemblyPath)) {
        Write-Host "  Assembly status: missing"
        Write-Host ""
        continue
    }

    $assemblyItem = Get-Item -LiteralPath $assemblyPath
    Write-Host "  Assembly status: found"
    Write-Host "  Assembly size: $($assemblyItem.Length) bytes"
    Write-Host "  Assembly modified: $($assemblyItem.LastWriteTime.ToString('yyyy-MM-dd HH:mm:ss'))"

    if ($assemblyItem.Length -lt 65536) {
        Write-Host "  Problem: DLL is very small and is probably a design-time stub."
    }

    if ([int]$RevitYear -ge 2025) {
        $depsPath = [System.IO.Path]::ChangeExtension($assemblyPath, ".deps.json")
        Write-Host "  Deps file: $depsPath"
        if (Test-Path -LiteralPath $depsPath) {
            $depsItem = Get-Item -LiteralPath $depsPath
            Write-Host "  Deps status: found ($($depsItem.Length) bytes)"
        }
        else {
            Write-Host "  Problem: missing .deps.json beside Revit 2025+ DLL."
        }
    }

    Write-Host ""
}

$duplicateRoots = @(
    (Join-Path $env:APPDATA ("Autodesk\Revit\Addins\" + $RevitYear)),
    (Join-Path $env:ProgramData ("Autodesk\Revit\Addins\" + $RevitYear))
) | Select-Object -Unique

Write-Host "Other CamboBIM/MHNK manifests:"
foreach ($root in $duplicateRoots) {
    if (-not (Test-Path -LiteralPath $root)) {
        continue
    }

    Get-ChildItem -LiteralPath $root -Filter "*.addin" -File |
        Where-Object { $_.Name -match "(?i)(CamboBIM|MHNK)" } |
        ForEach-Object { Write-Host "  $($_.FullName)" }
}
