param(
    [string]$Configuration = "Debug",
    [string]$Platform = "AnyCPU",
    [string]$RevitYear = "",
    [string]$AddInId = "d4265daa-0966-461a-8900-c48c10260677",
    [string]$AssemblyPath = "",
    [bool]$UseShadowCopy = $true,
    [string]$LicenseConfigPath = "",
    [string]$LicenseServerUrl = "",
    [switch]$SkipLicenseConfig = $false,
    [switch]$ForceLicenseConfig = $false,
    [switch]$AllUsers = $false,
    [switch]$UnlockTestUsers = $false,
    [switch]$AllowSmallRuntime = $false
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

if ([string]::IsNullOrWhiteSpace($RevitYear)) {
    $scriptStem = [System.IO.Path]::GetFileNameWithoutExtension($PSCommandPath)
    if (-not [string]::IsNullOrWhiteSpace($scriptStem) -and $scriptStem -match "(?i)revit(?<year>\d{4})-addin$") {
        $RevitYear = $Matches["year"]
    }
    else {
        $RevitYear = "2024"
    }
}

function Copy-DirectoryBestEffort {
    param(
        [Parameter(Mandatory = $true)][string]$SourceDir,
        [Parameter(Mandatory = $true)][string]$DestinationDir
    )

    if (-not (Test-Path -LiteralPath $SourceDir)) {
        throw "Source directory not found: $SourceDir"
    }

    if (-not (Test-Path -LiteralPath $DestinationDir)) {
        New-Item -Path $DestinationDir -ItemType Directory -Force | Out-Null
    }

    $skippedFiles = New-Object 'System.Collections.Generic.List[string]'

    # Copy file-by-file so one locked file does not block add-in deployment.
    Get-ChildItem -LiteralPath $SourceDir -Recurse -Force | ForEach-Object {
        $sourcePath = $_.FullName
        $relativePath = $sourcePath.Substring($SourceDir.Length).TrimStart('\')
        if ([string]::IsNullOrWhiteSpace($relativePath)) {
            return
        }

        $destPath = Join-Path $DestinationDir $relativePath

        if ($_.PSIsContainer) {
            if (-not (Test-Path -LiteralPath $destPath)) {
                New-Item -Path $destPath -ItemType Directory -Force | Out-Null
            }
            return
        }

        $destParent = Split-Path -Parent $destPath
        if (-not (Test-Path -LiteralPath $destParent)) {
            New-Item -Path $destParent -ItemType Directory -Force | Out-Null
        }

        try {
            Copy-Item -LiteralPath $sourcePath -Destination $destPath -Force -ErrorAction Stop
        }
        catch {
            $message = ""
            if ($_.Exception -and $_.Exception.Message) {
                $message = [string]$_.Exception.Message
            }
            if ($message -match "(?i)denied") {
                $skippedFiles.Add($sourcePath) | Out-Null
                Write-Warning ("Skipped locked file during shadow copy: " + $sourcePath)
                return
            }

            throw
        }
    }

    if ($skippedFiles.Count -gt 0) {
        Write-Warning ("Shadow copy completed with " + $skippedFiles.Count + " skipped locked file(s).")
    }
}

$projectRoot = Split-Path -Parent $PSScriptRoot
$addinName = "CamboBIM.Revit" + $RevitYear + ".Addin"
$templatePath = Join-Path $projectRoot ("addins\" + $addinName + ".addin.template")
$assemblyFileName = $addinName + ".dll"

if ([string]::IsNullOrWhiteSpace($AssemblyPath)) {
    $candidates = @()

    if (-not [string]::IsNullOrWhiteSpace($Platform) -and
        -not $Platform.Equals("AnyCPU", [StringComparison]::OrdinalIgnoreCase)) {
        $candidates += Join-Path $projectRoot ("bin\Revit" + $RevitYear + "\" + $Platform + "\" + $Configuration + "\" + $assemblyFileName)
        $candidates += Join-Path $projectRoot ("bin\" + $Platform + "\" + $Configuration + "\" + $assemblyFileName)
    }

    # Common output layouts used by VS / MSBuild depending on Platform.
    $candidates += Join-Path $projectRoot ("bin\Revit" + $RevitYear + "\" + $Configuration + "\" + $assemblyFileName)
    $candidates += Join-Path $projectRoot ("bin\Revit" + $RevitYear + "\x64\" + $Configuration + "\" + $assemblyFileName)
    $candidates += Join-Path $projectRoot ("bin\Revit" + $RevitYear + "\AnyCPU\" + $Configuration + "\" + $assemblyFileName)
    $candidates += Join-Path $projectRoot ("bin\" + $Configuration + "\" + $assemblyFileName)
    $candidates += Join-Path $projectRoot ("bin\x64\" + $Configuration + "\" + $assemblyFileName)
    $candidates += Join-Path $projectRoot ("bin\AnyCPU\" + $Configuration + "\" + $assemblyFileName)

    $existingCandidates = @($candidates | Where-Object { Test-Path $_ })
    if ($existingCandidates.Count -eq 0) {
        $checked = ($candidates | ForEach-Object { " - $_" }) -join [Environment]::NewLine
        throw "Build output not found. Build first. Paths checked:`n$checked"
    }

    # If multiple candidates exist, prefer the newest artifact.
    $assemblyPath = $existingCandidates |
        Sort-Object { (Get-Item $_).LastWriteTimeUtc } -Descending |
        Select-Object -First 1
}
else {
    if (-not (Test-Path $AssemblyPath)) {
        throw "AssemblyPath does not exist: $AssemblyPath"
    }
    $assemblyPath = $AssemblyPath
}

$assemblyPath = (Resolve-Path $assemblyPath).Path
$manifestAssemblyPath = $assemblyPath

function Assert-DeployableRuntime {
    param(
        [Parameter(Mandatory = $true)][string]$RuntimeAssemblyPath,
        [Parameter(Mandatory = $true)][string]$Year
    )

    if (-not (Test-Path -LiteralPath $RuntimeAssemblyPath)) {
        throw "Runtime assembly does not exist: $RuntimeAssemblyPath"
    }

    $assemblyItem = Get-Item -LiteralPath $RuntimeAssemblyPath
    if (-not $AllowSmallRuntime -and $assemblyItem.Length -lt 65536) {
        throw "Refusing to deploy a tiny DLL because it is probably a design-time stub: $RuntimeAssemblyPath ($($assemblyItem.Length) bytes). Build a real Revit $Year add-in on a PC with Revit API installed, then deploy the real Release|x64 DLL."
    }

    if ([int]$Year -ge 2025) {
        $depsPath = [System.IO.Path]::ChangeExtension($RuntimeAssemblyPath, ".deps.json")
        if (-not (Test-Path -LiteralPath $depsPath)) {
            throw "Revit $Year .NET add-in runtime is incomplete. Missing dependency file: $depsPath"
        }
    }
}

Assert-DeployableRuntime -RuntimeAssemblyPath $assemblyPath -Year $RevitYear

if ($UseShadowCopy) {
    $configToken = if ([string]::IsNullOrWhiteSpace($Configuration)) { "Debug" } else { $Configuration }
    $platformToken = if ([string]::IsNullOrWhiteSpace($Platform)) { "AnyCPU" } else { $Platform }
    $configToken = [Regex]::Replace($configToken, "[^A-Za-z0-9_-]", "_")
    $platformToken = [Regex]::Replace($platformToken, "[^A-Za-z0-9_-]", "_")
    $stamp = Get-Date -Format "yyyyMMdd_HHmmss_fff"

    $shadowRoot = Join-Path $env:LOCALAPPDATA ("CamboBIM\Revit\Addins\" + $RevitYear + "\dev")
    $shadowDir = Join-Path $shadowRoot ($configToken + "_" + $platformToken + "_" + $stamp)
    New-Item -Path $shadowDir -ItemType Directory -Force | Out-Null

    $buildOutputDir = Split-Path -Parent $assemblyPath
    Copy-DirectoryBestEffort -SourceDir $buildOutputDir -DestinationDir $shadowDir

    $shadowAssemblyPath = Join-Path $shadowDir $assemblyFileName
    if (-not (Test-Path $shadowAssemblyPath)) {
        throw "Shadow copy failed. Assembly not found in deployed folder: $shadowAssemblyPath"
    }

    $manifestAssemblyPath = (Resolve-Path $shadowAssemblyPath).Path

    # Keep only recent shadow deployments to avoid unbounded growth.
    $allShadowDirs = @(Get-ChildItem -Path $shadowRoot -Directory | Sort-Object LastWriteTimeUtc -Descending)
    if ($allShadowDirs.Count -gt 25) {
        $allShadowDirs | Select-Object -Skip 25 | ForEach-Object {
            try {
                Remove-Item -LiteralPath $_.FullName -Recurse -Force -ErrorAction Stop
            }
            catch {
                # ignore cleanup failures
            }
        }
    }
}


if (-not (Test-Path $templatePath)) {
    throw "Manifest template not found: $templatePath"
}

$revitAddinsRoot = if ($AllUsers) { $env:ProgramData } else { $env:APPDATA }
$revitAddinsDir = Join-Path $revitAddinsRoot ("Autodesk\Revit\Addins\" + $RevitYear)
if (-not (Test-Path $revitAddinsDir)) {
    New-Item -Path $revitAddinsDir -ItemType Directory | Out-Null
}

$manifestPath = Join-Path $revitAddinsDir ($addinName + ".addin")
$xml = Get-Content -Raw -Path $templatePath
$xml = $xml.Replace("{{ASSEMBLY_PATH}}", $manifestAssemblyPath)
$xml = $xml.Replace("{{ADDIN_ID}}", $AddInId)
Set-Content -Path $manifestPath -Value $xml -Encoding UTF8

function Disable-ManifestFile {
    param(
        [Parameter(Mandatory = $true)][string]$PathToDisable,
        [Parameter(Mandatory = $true)][string]$Reason
    )

    $disabledPath = $PathToDisable + ".disabled"
    if (Test-Path $disabledPath) {
        $stamp = Get-Date -Format "yyyyMMddHHmmss"
        $disabledPath = $PathToDisable + "." + $stamp + ".disabled"
    }

    try {
        Move-Item -Path $PathToDisable -Destination $disabledPath -Force
    }
    catch {
        throw "Failed to disable manifest '$PathToDisable' ($Reason). " +
              "This usually means ProgramData requires admin rights. " +
              "Run this script in an elevated PowerShell or disable it manually. Error: $($_.Exception.Message)"
    }

    Write-Host "Disabled manifest ($Reason):"
    Write-Host "  $PathToDisable -> $disabledPath"
}

$programDataAddinsDir = Join-Path $env:ProgramData ("Autodesk\Revit\Addins\" + $RevitYear)
$scanDirs = @($revitAddinsDir, $programDataAddinsDir) | Select-Object -Unique

# Revit fails on duplicated Application AddInId. Keep only the freshly deployed manifest.
foreach ($dir in $scanDirs) {
    if (-not (Test-Path $dir)) {
        continue
    }

    Get-ChildItem -Path $dir -File -Filter *.addin | ForEach-Object {
        $candidatePath = $_.FullName

        if ($candidatePath -eq $manifestPath) {
            return
        }

        $raw = Get-Content -Raw -Path $candidatePath
        if ($raw -match ("<AddInId>\s*" + [Regex]::Escape($AddInId) + "\s*</AddInId>")) {
            Disable-ManifestFile -PathToDisable $candidatePath -Reason "duplicate AddInId $AddInId"
            return
        }

        if ($_.Name -ieq "CamboBIM.addin") {
            Disable-ManifestFile -PathToDisable $candidatePath -Reason "legacy CamboBIM manifest"
        }
    }
}

Write-Host "Manifest deployed:"
Write-Host "  $manifestPath"
Write-Host "Assembly:"
Write-Host "  $manifestAssemblyPath"
if ($manifestAssemblyPath -ne $assemblyPath) {
    Write-Host "Build output:"
    Write-Host "  $assemblyPath"
}

if (-not $SkipLicenseConfig) {
    $licenseTargetDir = Join-Path $env:ProgramData "CamboBIM"
    $licenseTargetPath = Join-Path $licenseTargetDir "online-license.config.json"

    $licenseSourceCandidates = @()
    if (-not [string]::IsNullOrWhiteSpace($LicenseConfigPath)) {
        $licenseSourceCandidates += $LicenseConfigPath
    }

    $licenseSourceCandidates += Join-Path $projectRoot "license\online-license.config.json"
    $licenseSourceCandidates += Join-Path $projectRoot "license\online-license.google-sheet.sample.json"
    $licenseSourceCandidates += Join-Path $projectRoot "license\online-license.config.json.template"

    $licenseSource = $licenseSourceCandidates |
        Where-Object { -not [string]::IsNullOrWhiteSpace($_) -and (Test-Path -LiteralPath $_) } |
        Select-Object -First 1

    if (-not (Test-Path -LiteralPath $licenseTargetDir)) {
        New-Item -Path $licenseTargetDir -ItemType Directory | Out-Null
    }

    if ($UnlockTestUsers) {
        $unlockedConfig = [ordered]@{
            enabled                  = $false
            server_url               = ""
            product_code             = "CBIM_RVT" + $RevitYear + "_EXTENSION"
            activate_endpoint        = "?action=activate"
            heartbeat_endpoint       = "?action=heartbeat"
            release_endpoint         = "?action=release"
            change_password_endpoint = "?action=change_password"
            support_contact_email    = "support@cambobim.com"
            support_contact_phone    = "+855 69 901 004"
            support_contact_telegram = "@CamboBIMSupport"
            timeout_seconds          = 15
            heartbeat_seconds        = 300
        }
        ($unlockedConfig | ConvertTo-Json -Depth 10) | Set-Content -LiteralPath $licenseTargetPath -Encoding UTF8
        Write-Host "License config:"
        Write-Host "  $licenseTargetPath"
        Write-Host "License mode:"
        Write-Host "  unlocked test mode"
        Write-Host ""
        Write-Host "Restart Revit if it is currently open."
        return
    }

    $mustCopyTemplate = $ForceLicenseConfig -or -not (Test-Path -LiteralPath $licenseTargetPath)
    if ($mustCopyTemplate) {
        if ([string]::IsNullOrWhiteSpace($licenseSource)) {
            throw "No license config template found. Expected one of:`n - " + (($licenseSourceCandidates -join "`n - "))
        }

        Copy-Item -LiteralPath $licenseSource -Destination $licenseTargetPath -Force

        if (-not $RevitYear.Equals("2024", [StringComparison]::OrdinalIgnoreCase)) {
            try {
                $jsonRaw = Get-Content -Raw -LiteralPath $licenseTargetPath
                $jsonObj = ConvertFrom-Json -InputObject $jsonRaw
                if ($jsonObj.product_code -eq "CBIM_RVT2024_EXTENSION") {
                    $jsonObj.product_code = "CBIM_RVT" + $RevitYear + "_EXTENSION"
                    ($jsonObj | ConvertTo-Json -Depth 10) | Set-Content -LiteralPath $licenseTargetPath -Encoding UTF8
                }
            }
            catch {
                Write-Warning ("Could not update license product_code for Revit " + $RevitYear + ": " + $_.Exception.Message)
            }
        }
    }

    if (-not [string]::IsNullOrWhiteSpace($LicenseServerUrl)) {
        if (-not (Test-Path -LiteralPath $licenseTargetPath)) {
            throw "License config does not exist: $licenseTargetPath"
        }

        $jsonRaw = Get-Content -Raw -LiteralPath $licenseTargetPath
        $jsonObj = ConvertFrom-Json -InputObject $jsonRaw
        $jsonObj.server_url = $LicenseServerUrl
        ($jsonObj | ConvertTo-Json -Depth 10) | Set-Content -LiteralPath $licenseTargetPath -Encoding UTF8
    }

    Write-Host "License config:"
    Write-Host "  $licenseTargetPath"
    if (-not [string]::IsNullOrWhiteSpace($LicenseServerUrl)) {
        Write-Host "License server_url:"
        Write-Host "  $LicenseServerUrl"
    }
}
else {
    Write-Host "License config:"
    Write-Host "  skipped (SkipLicenseConfig=true)"
}

Write-Host ""
Write-Host "Restart Revit if it is currently open."
