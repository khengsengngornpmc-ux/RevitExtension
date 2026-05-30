param(
    [string]$DatabasePath = ".\database\RevitDBLink_FullTemplate.mdb"
)

$ErrorActionPreference = "Stop"

function Escape-Name([string]$name) {
    return "[" + ($name -replace "]", "]]") + "]"
}

function Table-Exists([System.Data.OleDb.OleDbConnection]$conn, [string]$tableName) {
    $tables = $conn.GetSchema("Tables") | Where-Object { $_.TABLE_TYPE -eq "TABLE" } | Select-Object -ExpandProperty TABLE_NAME
    return $tables -contains $tableName
}

function Execute-NonQuery([System.Data.OleDb.OleDbConnection]$conn, [string]$sql) {
    $cmd = $conn.CreateCommand()
    $cmd.CommandText = $sql
    [void]$cmd.ExecuteNonQuery()
}

$dbPath = if ([System.IO.Path]::IsPathRooted($DatabasePath)) {
    $DatabasePath
} else {
    [System.IO.Path]::GetFullPath((Join-Path (Get-Location).Path $DatabasePath))
}

if (-not (Test-Path -LiteralPath $dbPath)) {
    throw "Database not found: $dbPath"
}

$conn = New-Object System.Data.OleDb.OleDbConnection("Provider=Microsoft.ACE.OLEDB.12.0;Data Source=$dbPath;Persist Security Info=False;")
$conn.Open()

try {
    $tableNames = @(
        "Project_Info",
        "Sale_Info",
        "Site_Info",
        "Material_Info",
        "Planing_Info"
    )

    foreach ($tableName in $tableNames) {
        if (Table-Exists -conn $conn -tableName $tableName) {
            Write-Host "Skip existing table: $tableName"
            continue
        }

        $escaped = Escape-Name $tableName
        $sql = @"
CREATE TABLE $escaped (
    Id COUNTER CONSTRAINT PK_$tableName PRIMARY KEY,
    Code TEXT(100),
    Name TEXT(255),
    Description LONGTEXT,
    CreatedAt DATETIME,
    UpdatedAt DATETIME
)
"@

        Execute-NonQuery -conn $conn -sql $sql
        Write-Host "Created table: $tableName"
    }
}
finally {
    $conn.Close()
}

Write-Host "Done. Database updated:" $dbPath
