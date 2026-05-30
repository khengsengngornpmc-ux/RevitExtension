param(
    [string]$OutputPath = ".\database\RevitDBLink_Parameters.mdb",
    [string]$FloorsTemplatePath = ".\database\RevitDBLink_FullTemplate.mdb",
    [switch]$Force
)

$ErrorActionPreference = "Stop"

function New-DirectoryIfMissing([string]$filePath) {
    $dir = Split-Path -Parent $filePath
    if (-not (Test-Path -LiteralPath $dir)) {
        New-Item -ItemType Directory -Path $dir | Out-Null
    }
}

function Invoke-DbNonQuery([System.Data.OleDb.OleDbConnection]$conn, [string]$sql) {
    $cmd = $conn.CreateCommand()
    $cmd.CommandText = $sql
    [void]$cmd.ExecuteNonQuery()
}

function ConvertTo-SqlEscapedLiteral([string]$value) {
    return $value.Replace("'", "''")
}

function Test-TableExists([System.Data.OleDb.OleDbConnection]$conn, [string]$tableName) {
    $tables = $conn.GetSchema("Tables") |
        Where-Object { $_.TABLE_TYPE -eq "TABLE" } |
        Select-Object -ExpandProperty TABLE_NAME
    return $tables -contains $tableName
}

function Test-ColumnExists([System.Data.OleDb.OleDbConnection]$conn, [string]$tableName, [string]$columnName) {
    try {
        $cmd = $conn.CreateCommand()
        $cmd.CommandText = "SELECT TOP 1 * FROM [$tableName]"
        $reader = $cmd.ExecuteReader()
        try {
            $schema = $reader.GetSchemaTable()
            foreach ($row in $schema.Rows) {
                $name = [string]$row["ColumnName"]
                if ($name -eq $columnName) {
                    return $true
                }
            }
        } finally {
            if ($reader) { $reader.Close() }
        }
    } catch {
    }

    return $false
}

function Add-MissingFloorsColumns([System.Data.OleDb.OleDbConnection]$conn) {
    $requiredColumns = @(
        @{ Name = "HOUSE ID"; Type = "TEXT(255)" },
        @{ Name = "ZONE"; Type = "TEXT(255)" },
        @{ Name = "BLOCK"; Type = "TEXT(255)" },
        @{ Name = "SUB-BLOCK"; Type = "TEXT(255)" },
        @{ Name = "HOUSE-TYPE"; Type = "TEXT(255)" },
        @{ Name = "SOW"; Type = "TEXT(255)" },
        @{ Name = "Code_Item"; Type = "TEXT(255)" },
        @{ Name = "%Site_Progress"; Type = "DOUBLE" },
        @{ Name = "Handover Date"; Type = "TEXT(255)" },
        @{ Name = "NÂº"; Type = "LONG" },
        @{ Name = "Priority"; Type = "TEXT(255)" },
        @{ Name = "3D-HOUSE TYPE & ID"; Type = "TEXT(255)" },
        @{ Name = "Code_LOA/BLC/VO"; Type = "TEXT(255)" },
        @{ Name = "HOUSE_GROUP_TYPE"; Type = "TEXT(255)" },
        @{ Name = "Comments1"; Type = "TEXT(255)" },
        @{ Name = "Comments2"; Type = "TEXT(255)" },
        @{ Name = "Comments3"; Type = "TEXT(255)" },
        @{ Name = "MR_DOOR ACCESS"; Type = "TEXT(255)" },
        @{ Name = "House No(Sell)"; Type = "TEXT(255)" },
        @{ Name = "Building left"; Type = "LONG" },
        @{ Name = "Building Right"; Type = "LONG" },
        @{ Name = "REMARK/SCOPE"; Type = "TEXT(255)" },
        @{ Name = "HANDOVERED"; Type = "LONG" },
        @{ Name = "BUDGET COST"; Type = "TEXT(255)" },
        @{ Name = "End Date SPA+ GP"; Type = "TEXT(255)" },
        @{ Name = "House-Units"; Type = "LONG" },
        @{ Name = "Handovered-Units"; Type = "LONG" },
        @{ Name = "Data_Sold_Out"; Type = "LONG" },
        @{ Name = "LAND LOTS"; Type = "LONG" },
        @{ Name = "Sold Only Land"; Type = "LONG" },
        @{ Name = "Sold/Unsold"; Type = "TEXT(255)" },
        @{ Name = "Construction Type"; Type = "TEXT(255)" },
        @{ Name = "Plan Description"; Type = "TEXT(255)" },
        @{ Name = "HANDOVERED STATUS"; Type = "TEXT(255)" },
        @{ Name = "TOC (Lyna)"; Type = "TEXT(255)" },
        @{ Name = "Plan Handover"; Type = "TEXT(255)" },
        @{ Name = "SPA Date"; Type = "TEXT(255)" },
        @{ Name = "SPA (HO Date)"; Type = "TEXT(255)" },
        @{ Name = "SPA+GP"; Type = "TEXT(255)" },
        @{ Name = "Priority build"; Type = "TEXT(255)" },
        @{ Name = "House Priority"; Type = "TEXT(255)" }
    )

    foreach ($col in $requiredColumns) {
        if (-not (Test-ColumnExists -conn $conn -tableName "Floors" -columnName $col.Name)) {
            Invoke-DbNonQuery $conn "ALTER TABLE Floors ADD COLUMN [$($col.Name)] $($col.Type)"
        }
    }
}

function New-FallbackFloorsTable([System.Data.OleDb.OleDbConnection]$conn) {
    Invoke-DbNonQuery $conn @"
CREATE TABLE Floors (
    [Id] LONG NOT NULL,
    [TypeId] LONG,
    [PhaseCreated] LONG,
    [PhaseDemolished] LONG,
    [DesignOption] LONG,
    [EstimatedReinforcementVolume] DOUBLE,
    [Volume] DOUBLE,
    [Area] DOUBLE,
    [Comments] TEXT(255),
    [Level] LONG,
    [Structural] LONG,
    [Perimeter] DOUBLE,
    [HeightOffsetFromLevel] DOUBLE,
    [AnalyzeAs] LONG,
    [Mark] TEXT(255),
    [HOUSE ID] TEXT(255),
    [ZONE] TEXT(255),
    [BLOCK] TEXT(255),
    [SUB-BLOCK] TEXT(255),
    [HOUSE-TYPE] TEXT(255),
    [SOW] TEXT(255),
    [Code_Item] TEXT(255),
    [%Site_Progress] DOUBLE,
    [Handover Date] TEXT(255),
    [Nº] LONG,
    [Priority] TEXT(255),
    [3D-HOUSE TYPE & ID] TEXT(255),
    [Code_LOA/BLC/VO] TEXT(255),
    [HOUSE_GROUP_TYPE] TEXT(255),
    [Comments1] TEXT(255),
    [Comments2] TEXT(255),
    [Comments3] TEXT(255),
    [MR_DOOR ACCESS] TEXT(255),
    [House No(Sell)] TEXT(255),
    [Building left] LONG,
    [Building Right] LONG,
    [REMARK/SCOPE] TEXT(255),
    [HANDOVERED] LONG,
    [BUDGET COST] TEXT(255),
    [End Date SPA+ GP] TEXT(255),
    [House-Units] LONG,
    [Handovered-Units] LONG,
    [Data_Sold_Out] LONG,
    [LAND LOTS] LONG,
    [Sold Only Land] LONG,
    [Sold/Unsold] TEXT(255),
    [Construction Type] TEXT(255),
    [Plan Description] TEXT(255),
    [HANDOVERED STATUS] TEXT(255),
    [TOC (Lyna)] TEXT(255),
    [Plan Handover] TEXT(255),
    [SPA Date] TEXT(255),
    [SPA (HO Date)] TEXT(255),
    [SPA+GP] TEXT(255),
    [Priority build] TEXT(255),
    [House Priority] TEXT(255),
    CONSTRAINT PK_Floors PRIMARY KEY ([Id])
)
"@
}

function Get-DbKind([string]$path) {
    $ext = [System.IO.Path]::GetExtension($path).ToLowerInvariant()
    if ($ext -eq ".mdb") { return "mdb" }
    if ($ext -eq ".accdb") { return "accdb" }
    throw "Unsupported extension '$ext'. Use .mdb or .accdb."
}

$resolved = Resolve-Path -LiteralPath "." | Select-Object -ExpandProperty Path
$dbPath = if ([System.IO.Path]::IsPathRooted($OutputPath)) {
    $OutputPath
} else {
    [System.IO.Path]::GetFullPath((Join-Path $resolved $OutputPath))
}

$templatePath = if ([System.IO.Path]::IsPathRooted($FloorsTemplatePath)) {
    $FloorsTemplatePath
} else {
    [System.IO.Path]::GetFullPath((Join-Path $resolved $FloorsTemplatePath))
}

New-DirectoryIfMissing -filePath $dbPath
$dbKind = Get-DbKind -path $dbPath

if ((Test-Path -LiteralPath $dbPath) -and -not $Force.IsPresent) {
    throw "Database already exists: $dbPath. Use -Force to recreate."
}

if (Test-Path -LiteralPath $dbPath) {
    Remove-Item -LiteralPath $dbPath -Force
}

# Create database via ADOX (ACE for .accdb, Jet/ACE fallback for .mdb)
$catalog = New-Object -ComObject ADOX.Catalog
if ($dbKind -eq "accdb") {
    $catalog.Create("Provider=Microsoft.ACE.OLEDB.12.0;Data Source=$dbPath;Jet OLEDB:Engine Type=6")
    $provider = "Microsoft.ACE.OLEDB.12.0"
} else {
    try {
        $catalog.Create("Provider=Microsoft.Jet.OLEDB.4.0;Data Source=$dbPath;Jet OLEDB:Engine Type=5")
        $provider = "Microsoft.Jet.OLEDB.4.0"
    } catch {
        $catalog.Create("Provider=Microsoft.ACE.OLEDB.12.0;Data Source=$dbPath;Jet OLEDB:Engine Type=5")
        $provider = "Microsoft.ACE.OLEDB.12.0"
    }
}

$conn = New-Object System.Data.OleDb.OleDbConnection("Provider=$provider;Data Source=$dbPath;Persist Security Info=False;")
$conn.Open()

try {
    Invoke-DbNonQuery $conn @"
CREATE TABLE Projects (
    ProjectGuid TEXT(64) NOT NULL,
    ProjectName TEXT(255),
    RevitVersion TEXT(32),
    ModelPath LONGTEXT,
    CreatedAt DATETIME,
    UpdatedAt DATETIME,
    CONSTRAINT PK_Projects PRIMARY KEY (ProjectGuid)
)
"@

    Invoke-DbNonQuery $conn @"
CREATE TABLE Elements (
    ElementId LONG NOT NULL,
    UniqueId TEXT(255),
    ProjectGuid TEXT(64),
    Category TEXT(128),
    FamilyName TEXT(255),
    TypeName TEXT(255),
    LevelName TEXT(255),
    Workset TEXT(255),
    Mark TEXT(128),
    Comments LONGTEXT,
    LastSeen DATETIME,
    CONSTRAINT PK_Elements PRIMARY KEY (ElementId)
)
"@

    Invoke-DbNonQuery $conn @"
CREATE TABLE ParameterDefinitions (
    ParamGuid TEXT(64) NOT NULL,
    ParamName TEXT(255) NOT NULL,
    ParamGroup TEXT(128),
    ParamType TEXT(64),
    IsShared YESNO,
    IsInstance YESNO,
    BuiltInName TEXT(255),
    CONSTRAINT PK_ParameterDefinitions PRIMARY KEY (ParamGuid)
)
"@

    Invoke-DbNonQuery $conn @"
CREATE TABLE ElementParameters (
    Id COUNTER CONSTRAINT PK_ElementParameters PRIMARY KEY,
    ElementId LONG NOT NULL,
    ParamGuid TEXT(64),
    ParamName TEXT(255) NOT NULL,
    ParamGroup TEXT(128),
    StorageType TEXT(32),
    UnitSymbol TEXT(64),
    ParamValue LONGTEXT
)
"@

    Invoke-DbNonQuery $conn @"
CREATE TABLE ElementQuantities (
    Id COUNTER CONSTRAINT PK_ElementQuantities PRIMARY KEY,
    ElementId LONG NOT NULL,
    QuantityName TEXT(128) NOT NULL,
    QuantityValue DOUBLE,
    UnitSymbol TEXT(64)
)
"@

    Invoke-DbNonQuery $conn "CREATE INDEX IX_Elements_ProjectGuid ON Elements (ProjectGuid)"
    Invoke-DbNonQuery $conn "CREATE UNIQUE INDEX IX_Elements_UniqueId ON Elements (UniqueId)"
    Invoke-DbNonQuery $conn "CREATE UNIQUE INDEX IX_ParameterDefinitions_Name ON ParameterDefinitions (ParamName)"
    Invoke-DbNonQuery $conn "CREATE INDEX IX_ElementParameters_ElementId ON ElementParameters (ElementId)"
    Invoke-DbNonQuery $conn "CREATE INDEX IX_ElementParameters_ParamGuid ON ElementParameters (ParamGuid)"
    Invoke-DbNonQuery $conn "CREATE INDEX IX_ElementQuantities_ElementId ON ElementQuantities (ElementId)"

    # Add Floors table for Borey/site-progress workflows.
    if (-not (Test-TableExists -conn $conn -tableName "Floors")) {
        $createdFromTemplate = $false
        if (Test-Path -LiteralPath $templatePath) {
            try {
                $escapedTemplatePath = ConvertTo-SqlEscapedLiteral $templatePath
                Invoke-DbNonQuery $conn "SELECT TOP 0 * INTO Floors FROM Floors IN '$escapedTemplatePath'"
                if (Test-ColumnExists -conn $conn -tableName "Floors" -columnName "Id") {
                    Invoke-DbNonQuery $conn "CREATE UNIQUE INDEX IX_Floors_Id ON Floors ([Id])"
                }
                $createdFromTemplate = $true
                Write-Host "Created Floors table from template:" $templatePath
            } catch {
                Write-Warning "Failed to clone Floors schema from template '$templatePath'. Falling back to built-in schema. Error: $($_.Exception.Message)"
            }
        }

        if (-not $createdFromTemplate) {
            New-FallbackFloorsTable -conn $conn
            Write-Host "Created Floors table from built-in fallback schema."
        }
    }

    # Ensure Borey columns exist even if Floors came from a minimal DBLink template.
    Add-MissingFloorsColumns -conn $conn
}
finally {
    $conn.Close()
}

Write-Host "Created Access database:" $dbPath
Write-Host "Provider:" $provider
