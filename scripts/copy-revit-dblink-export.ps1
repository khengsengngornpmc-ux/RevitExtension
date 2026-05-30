param(
    [string]$SourcePath = "E:\CamboBIM\CamboBIM\DBLINK.mdb",
    [string]$OutputPath = ".\database\RevitDBLink_Export.mdb",
    [switch]$Force
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path -LiteralPath $SourcePath)) {
    throw "Source DBLink file not found: $SourcePath"
}

$resolved = Resolve-Path -LiteralPath "." | Select-Object -ExpandProperty Path
$targetPath = if ([System.IO.Path]::IsPathRooted($OutputPath)) {
    $OutputPath
} else {
    [System.IO.Path]::GetFullPath((Join-Path $resolved $OutputPath))
}

$targetDir = Split-Path -Parent $targetPath
if (-not (Test-Path -LiteralPath $targetDir)) {
    New-Item -ItemType Directory -Path $targetDir | Out-Null
}

if ((Test-Path -LiteralPath $targetPath) -and -not $Force.IsPresent) {
    throw "Target exists: $targetPath. Use -Force to overwrite."
}

Copy-Item -LiteralPath $SourcePath -Destination $targetPath -Force

$conn = New-Object System.Data.OleDb.OleDbConnection("Provider=Microsoft.ACE.OLEDB.12.0;Data Source=$targetPath;Persist Security Info=False;")
$conn.Open()
try {
    $tableCount = ($conn.GetSchema("Tables") | Where-Object { $_.TABLE_TYPE -eq "TABLE" }).Count
}
finally {
    $conn.Close()
}

Write-Host "Copied Revit DBLink export to:" $targetPath
Write-Host "Tables:" $tableCount
