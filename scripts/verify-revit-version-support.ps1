param(
    [switch]$DesignTimeBuild,
    [switch]$CheckRevitApi,
    [switch]$Quiet
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$projectRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$years = @("2023", "2024", "2025", "2026", "2027")
$expectedFrameworkByYear = @{
    "2023" = "net48"
    "2024" = "net48"
    "2025" = "net8.0-windows10.0.19041.0"
    "2026" = "net8.0-windows10.0.19041.0"
    "2027" = "net10.0-windows10.0.19041.0"
}

$errors = New-Object 'System.Collections.Generic.List[string]'
$warnings = New-Object 'System.Collections.Generic.List[string]'

function Write-Step {
    param([Parameter(Mandatory = $true)][string]$Message)

    if (-not $Quiet) {
        Write-Host $Message
    }
}

function Add-Error {
    param([Parameter(Mandatory = $true)][string]$Message)

    $errors.Add($Message) | Out-Null
}

function Add-Warning {
    param([Parameter(Mandatory = $true)][string]$Message)

    $warnings.Add($Message) | Out-Null
}

function Get-SingleXmlValue {
    param(
        [Parameter(Mandatory = $true)][xml]$Xml,
        [Parameter(Mandatory = $true)][string]$Name
    )

    $nodes = @($Xml.SelectNodes("//PropertyGroup/$Name"))
    $values = @($nodes | ForEach-Object { $_.InnerText } | Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    if ($values.Count -eq 0) {
        return ""
    }

    return [string]$values[0]
}

function Test-ProjectExplicitIncludes {
    param(
        [Parameter(Mandatory = $true)][string]$ProjectPath,
        [Parameter(Mandatory = $true)][xml]$Xml
    )

    $items = @($Xml.SelectNodes("//ItemGroup/Compile[@Include]")) +
             @($Xml.SelectNodes("//ItemGroup/Page[@Include]")) +
             @($Xml.SelectNodes("//ItemGroup/Content[@Include]")) +
             @($Xml.SelectNodes("//ItemGroup/Resource[@Include]"))

    foreach ($item in $items) {
        $include = [string]$item.Include
        if ($include -like "*`**" -or $include -like "*`*") {
            continue
        }

        $path = Join-Path $projectRoot $include
        if (-not (Test-Path -LiteralPath $path)) {
            Add-Error "$(Split-Path -Leaf $ProjectPath): missing explicit include '$include'"
        }
    }
}

function Get-ProjectItemNodes {
    param([Parameter(Mandatory = $true)][xml]$Xml)

    return @($Xml.SelectNodes("//ItemGroup/Compile[@Include]")) +
           @($Xml.SelectNodes("//ItemGroup/Page[@Include]")) +
           @($Xml.SelectNodes("//ItemGroup/Content[@Include]")) +
           @($Xml.SelectNodes("//ItemGroup/Resource[@Include]"))
}

function Test-RequiredFile {
    param(
        [Parameter(Mandatory = $true)][string]$RelativePath,
        [Parameter(Mandatory = $true)][string]$Label
    )

    $path = Join-Path $projectRoot $RelativePath
    if (-not (Test-Path -LiteralPath $path)) {
        Add-Error "$Label missing: $RelativePath"
    }
}

function Test-ManifestTemplate {
    param([Parameter(Mandatory = $true)][string]$Year)

    $relativePath = "addins\CamboBIM.Revit$Year.Addin.addin.template"
    $path = Join-Path $projectRoot $relativePath
    if (-not (Test-Path -LiteralPath $path)) {
        Add-Error "Manifest template missing: $relativePath"
        return
    }

    $raw = Get-Content -Raw -LiteralPath $path
    if ($raw -notmatch "<Name>\s*MHNK\.Revit$Year\.Addin\s*</Name>") {
        Add-Error "$relativePath has the wrong Name value."
    }

    if ($raw -notmatch "<FullClassName>\s*CamboBIM\.Revit2024\.Addin\.App\s*</FullClassName>") {
        Add-Error "$relativePath must point to CamboBIM.Revit2024.Addin.App until the shared namespace is migrated."
    }
}

function Get-ComparableProjectItems {
    param([Parameter(Mandatory = $true)][xml]$Xml)

    $nodes = Get-ProjectItemNodes -Xml $Xml

    $items = New-Object 'System.Collections.Generic.List[string]'
    foreach ($node in $nodes) {
        $include = [string]$node.Include
        if ($include -match "^Properties\\Revit\d{4}MissingApiDesignTimeStub\.cs$") {
            continue
        }

        $dependentUponNode = $node.SelectSingleNode("DependentUpon")
        $copyNode = $node.SelectSingleNode("CopyToOutputDirectory")
        $subTypeNode = $node.SelectSingleNode("SubType")
        $generatorNode = $node.SelectSingleNode("Generator")

        $dependentUpon = if ($null -eq $dependentUponNode) { "" } else { $dependentUponNode.InnerText }
        $copyToOutput = if ($null -eq $copyNode) { "" } else { $copyNode.InnerText }
        $subType = if ($null -eq $subTypeNode) { "" } else { $subTypeNode.InnerText }
        $generator = if ($null -eq $generatorNode) { "" } else { $generatorNode.InnerText }

        $items.Add("$($node.Name)|$include|$dependentUpon|$copyToOutput|$subType|$generator") | Out-Null
    }

    return @($items | Sort-Object)
}

function Test-SharedProjectItemsFile {
    $relativePath = "CamboBIM.SharedProjectItems.targets"
    $path = Join-Path $projectRoot $relativePath
    if (-not (Test-Path -LiteralPath $path)) {
        Add-Error "Shared project item registry missing: $relativePath"
        return
    }

    [xml]$xml = Get-Content -LiteralPath $path
    $items = @(Get-ComparableProjectItems -Xml $xml)
    if ($items.Count -eq 0) {
        Add-Error "$relativePath has no shared Compile/Page/Content/Resource items."
    }

    Test-ProjectExplicitIncludes -ProjectPath $path -Xml $xml
}

function Test-SharedProjectItemImports {
    param([Parameter(Mandatory = $true)][hashtable]$ProjectXmlByYear)

    foreach ($year in $years) {
        $xml = $ProjectXmlByYear[$year]
        $importNode = $xml.SelectSingleNode("//Import[@Project='CamboBIM.SharedProjectItems.targets']")
        if ($null -eq $importNode) {
            Add-Error "CamboBIM.Revit$year.Addin.csproj does not import CamboBIM.SharedProjectItems.targets."
        }

        $projectLocalItems = @(Get-ComparableProjectItems -Xml $xml)
        if ($projectLocalItems.Count -gt 0) {
            foreach ($item in $projectLocalItems) {
                Add-Error "CamboBIM.Revit$year.Addin.csproj should not keep shared item locally: $item"
            }
        }
    }
}

function Test-ProjectFile {
    param([Parameter(Mandatory = $true)][string]$Year)

    $relativePath = "CamboBIM.Revit$Year.Addin.csproj"
    $projectPath = Join-Path $projectRoot $relativePath
    if (-not (Test-Path -LiteralPath $projectPath)) {
        Add-Error "Project file missing: $relativePath"
        return
    }

    [xml]$xml = Get-Content -LiteralPath $projectPath
    $targetFramework = Get-SingleXmlValue -Xml $xml -Name "TargetFramework"
    if ($targetFramework -ne $expectedFrameworkByYear[$Year]) {
        Add-Error "$relativePath targets '$targetFramework' but expected '$($expectedFrameworkByYear[$Year])'."
    }

    $assemblyName = Get-SingleXmlValue -Xml $xml -Name "AssemblyName"
    if ($assemblyName -ne "CamboBIM.Revit$Year.Addin") {
        Add-Error "$relativePath AssemblyName is '$assemblyName'."
    }

    $rootNamespace = Get-SingleXmlValue -Xml $xml -Name "RootNamespace"
    if ($Year -in @("2023", "2024")) {
        $expectedRootNamespace = "CamboBIM.Revit$Year.Addin"
    }
    else {
        $expectedRootNamespace = "CamboBIM.Revit2024.Addin"
    }

    if ($rootNamespace -ne $expectedRootNamespace) {
        Add-Error "$relativePath RootNamespace is '$rootNamespace' but expected '$expectedRootNamespace'."
    }

    $defineConstants = @($xml.SelectNodes("//PropertyGroup/DefineConstants") |
        ForEach-Object { $_.InnerText } |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    if ($defineConstants.Count -eq 0) {
        Add-Error "$relativePath has no DefineConstants values."
    }

    foreach ($constantSet in $defineConstants) {
        $text = [string]$constantSet
        if ($text -notmatch "(^|;)REVIT$Year(;|$)") {
            Add-Error "$relativePath DefineConstants '$text' is missing REVIT$Year."
        }

        if ([int]$Year -ge 2025 -and $text -notmatch "(^|;)REVIT2025_OR_GREATER(;|$)") {
            Add-Error "$relativePath DefineConstants '$text' is missing REVIT2025_OR_GREATER."
        }
    }

    $outputPaths = @($xml.SelectNodes("//PropertyGroup/OutputPath") |
        ForEach-Object { $_.InnerText } |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) })
    foreach ($outputPath in $outputPaths) {
        if ([string]$outputPath -notmatch "bin\\Revit$Year\\") {
            Add-Error "$relativePath OutputPath '$outputPath' does not include bin\Revit$Year\."
        }
    }

    $deployText = Get-Content -Raw -LiteralPath $projectPath
    if ($deployText -notmatch "deploy-revit$Year-addin\.ps1") {
        Add-Error "$relativePath does not call the Revit $Year deploy wrapper."
    }

    if ($deployText -notmatch "FailWhenRevit$Year`ApiMissing") {
        Add-Error "$relativePath does not have a Revit $Year API missing fail target."
    }

    Test-ProjectExplicitIncludes -ProjectPath $projectPath -Xml $xml
}

Push-Location $projectRoot
try {
    Write-Step "Checking Revit host project matrix..."

    Test-RequiredFile -RelativePath "CamboBIM.AllRevitVersions.sln" -Label "All-version solution"
    Test-RequiredFile -RelativePath "docs\REVIT_VERSION_SUPPORT.md" -Label "Version support doc"
    Test-RequiredFile -RelativePath "scripts\add-shared-project-item.ps1" -Label "Shared item helper"
    Test-RequiredFile -RelativePath "scripts\build-deploy-exe-all-revit.ps1" -Label "All-version deploy EXE builder"
    Test-RequiredFile -RelativePath "scripts\build-inno-installer.ps1" -Label "Inno installer builder"
    Test-RequiredFile -RelativePath "scripts\build-inno-installer-all-revit.ps1" -Label "All-version Inno installer builder"
    Test-RequiredFile -RelativePath "installer\CamboBIM.Revit2024.Deploy.iss" -Label "Parameterized Inno template"
    Test-RequiredFile -RelativePath "Infrastructure\Composition\ExtensionServiceBootstrapper.cs" -Label "Composition bootstrapper"
    Test-RequiredFile -RelativePath "Infrastructure\Composition\ExtensionServiceRegistry.cs" -Label "Service registry"
    Test-RequiredFile -RelativePath "Infrastructure\RevitExecution\RevitExecutionBoundary.cs" -Label "Revit execution boundary"
    Test-RequiredFile -RelativePath "Infrastructure\RevitExecution\RevitTransactionRunner.cs" -Label "Revit transaction runner"
    Test-RequiredFile -RelativePath "Infrastructure\Security\ExternalInputValidator.cs" -Label "External input validator"
    Test-RequiredFile -RelativePath "Features\PTDrawing\PtDrawingImportWorkflowService.cs" -Label "PT import workflow service"
    Test-SharedProjectItemsFile

    $projectXmlByYear = @{}
    foreach ($year in $years) {
        Test-ProjectFile -Year $year
        $projectXmlByYear[$year] = [xml](Get-Content -LiteralPath (Join-Path $projectRoot "CamboBIM.Revit$year.Addin.csproj"))
        Test-ManifestTemplate -Year $year
        Test-RequiredFile -RelativePath "scripts\deploy-revit$year-addin.ps1" -Label "Deploy wrapper"
        Test-RequiredFile -RelativePath "scripts\reload-revit$year-addin.ps1" -Label "Reload wrapper"
        Test-RequiredFile -RelativePath "scripts\build-inno-installer-revit$year.ps1" -Label "Inno wrapper"

        if ($CheckRevitApi) {
            $apiPath = "C:\Program Files\Autodesk\Revit $year\RevitAPI.dll"
            if (-not (Test-Path -LiteralPath $apiPath)) {
                Add-Warning "Revit $year API not found on this PC: $apiPath"
            }
        }
    }

    Test-SharedProjectItemImports -ProjectXmlByYear $projectXmlByYear

    $solutionList = & dotnet sln .\CamboBIM.AllRevitVersions.sln list
    foreach ($year in $years) {
        if (($solutionList -join "`n") -notmatch "CamboBIM\.Revit$year\.Addin\.csproj") {
            Add-Error "CamboBIM.AllRevitVersions.sln does not include Revit $year project."
        }
    }

    if ($DesignTimeBuild) {
        foreach ($year in $years) {
            Write-Step "Design-time build check: Revit $year"
            & dotnet build ".\CamboBIM.Revit$year.Addin.csproj" -c Debug -p:Platform=x64 -p:DisableRevitDeploy=true -p:DesignTimeBuild=true -v quiet
            if ($LASTEXITCODE -ne 0) {
                Add-Error "Design-time build failed for Revit $year."
            }
        }
    }

    if ($warnings.Count -gt 0 -and -not $Quiet) {
        Write-Host ""
        Write-Host "Warnings:"
        foreach ($warning in $warnings) {
            Write-Host " - $warning"
        }
    }

    if ($errors.Count -gt 0) {
        Write-Host ""
        Write-Host "Version support verification failed:"
        foreach ($errorMessage in $errors) {
            Write-Host " - $errorMessage"
        }

        exit 1
    }

    if (-not $Quiet) {
        Write-Host ""
        Write-Host "Revit 2023-2027 version support verification passed."
    }
}
finally {
    Pop-Location
}
