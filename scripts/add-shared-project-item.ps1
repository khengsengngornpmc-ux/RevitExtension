param(
    [Parameter(Mandatory = $true)]
    [ValidateSet("Compile", "Page", "Content", "Resource")]
    [string]$ItemType,

    [Parameter(Mandatory = $true)]
    [string]$Include,

    [string]$DependentUpon = "",
    [string]$CopyToOutputDirectory = "",
    [string]$SubType = "",
    [string]$Generator = "",
    [switch]$WhatIf
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$projectRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$targetsPath = Join-Path $projectRoot "CamboBIM.SharedProjectItems.targets"

function Normalize-IncludePath {
    param([Parameter(Mandatory = $true)][string]$Path)

    $normalized = $Path.Trim().Trim('"').Trim("'").Replace("/", "\")
    while ($normalized.StartsWith(".\")) {
        $normalized = $normalized.Substring(2)
    }

    return $normalized
}

function Test-IncludePathExists {
    param([Parameter(Mandatory = $true)][string]$Path)

    if ($Path -like "*`**" -or $Path -like "*`*") {
        return
    }

    $fullPath = Join-Path $projectRoot $Path
    if (-not (Test-Path -LiteralPath $fullPath)) {
        throw "Included file does not exist: $Path"
    }
}

function Get-ItemGroupForType {
    param(
        [Parameter(Mandatory = $true)][xml]$Xml,
        [Parameter(Mandatory = $true)][string]$Type
    )

    $groups = @($Xml.SelectNodes("//ItemGroup[@Condition=""'`$(RevitApiAvailable)' == 'true'""][$Type]"))

    if ($groups.Count -gt 0) {
        return $groups[0]
    }

    $group = $Xml.CreateElement("ItemGroup")
    $condition = $Xml.CreateAttribute("Condition")
    $condition.Value = "'`$(RevitApiAvailable)' == 'true'"
    [void]$group.Attributes.Append($condition)
    [void]$Xml.DocumentElement.AppendChild($group)
    return $group
}

function Set-MetadataElement {
    param(
        [Parameter(Mandatory = $true)][System.Xml.XmlElement]$Item,
        [Parameter(Mandatory = $true)][string]$Name,
        [AllowEmptyString()][string]$Value
    )

    $existing = $Item.SelectSingleNode($Name)
    if ([string]::IsNullOrWhiteSpace($Value)) {
        if ($null -ne $existing) {
            [void]$Item.RemoveChild($existing)
        }

        return
    }

    if ($null -eq $existing) {
        $existing = $Item.OwnerDocument.CreateElement($Name)
        [void]$Item.AppendChild($existing)
    }

    $existing.InnerText = $Value.Trim()
}

function Format-XmlFile {
    param([Parameter(Mandatory = $true)][string]$Path)

    $doc = New-Object System.Xml.XmlDocument
    $doc.PreserveWhitespace = $false
    $doc.Load($Path)

    $settings = New-Object System.Xml.XmlWriterSettings
    $settings.Indent = $true
    $settings.IndentChars = "  "
    $settings.Encoding = New-Object System.Text.UTF8Encoding($false)
    $settings.NewLineChars = "`r`n"
    $settings.NewLineHandling = [System.Xml.NewLineHandling]::Replace

    $writer = [System.Xml.XmlWriter]::Create($Path, $settings)
    try {
        $doc.Save($writer)
    }
    finally {
        $writer.Close()
    }
}

if (-not (Test-Path -LiteralPath $targetsPath)) {
    throw "Shared project items file not found: $targetsPath"
}

$includePath = Normalize-IncludePath -Path $Include
Test-IncludePathExists -Path $includePath

[xml]$xml = Get-Content -LiteralPath $targetsPath
$itemGroup = Get-ItemGroupForType -Xml $xml -Type $ItemType
$existingItems = @($xml.SelectNodes("//ItemGroup/$ItemType[@Include]") | Where-Object {
    [string]::Equals([string]$_.Include, $includePath, [StringComparison]::OrdinalIgnoreCase)
})

if ($existingItems.Count -gt 0) {
    $item = [System.Xml.XmlElement]$existingItems[0]
    Write-Host "Updating existing $ItemType item:"
}
else {
    $item = $xml.CreateElement($ItemType)
    $includeAttr = $xml.CreateAttribute("Include")
    $includeAttr.Value = $includePath
    [void]$item.Attributes.Append($includeAttr)
    [void]$itemGroup.AppendChild($item)
    Write-Host "Adding $ItemType item:"
}

Write-Host "  $includePath"

Set-MetadataElement -Item $item -Name "DependentUpon" -Value $DependentUpon
Set-MetadataElement -Item $item -Name "CopyToOutputDirectory" -Value $CopyToOutputDirectory
Set-MetadataElement -Item $item -Name "SubType" -Value $SubType
Set-MetadataElement -Item $item -Name "Generator" -Value $Generator

if ($WhatIf) {
    Write-Host "WhatIf: no changes written."
    exit 0
}

$xml.Save($targetsPath)
Format-XmlFile -Path $targetsPath

Write-Host "Updated:"
Write-Host "  $targetsPath"
