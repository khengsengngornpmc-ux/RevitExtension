param(
    [string]$SourceDir = "",
    [switch]$IncludeAddinRuntime = $true,
    [string]$RuntimeDir = "",
    [string]$OutputDir = ".\\release\\inno",
    [string]$ScriptPath = ".\\installer\\CamboBIM.Revit2024.Deploy.iss",
    [string]$IsccPath = "",
    [ValidatePattern("^\d{4}$")]
    [string]$RevitYear = "2024",
    [switch]$AllowSmallRuntime
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
$projectRoot = Split-Path -Parent $PSScriptRoot

function Copy-DirectoryBestEffort {
    param(
        [Parameter(Mandatory = $true)][string]$SourceDir,
        [Parameter(Mandatory = $true)][string]$DestinationDir
    )

    if (-not (Test-Path -LiteralPath $SourceDir)) {
        throw "Source directory not found: $SourceDir"
    }

    if (-not (Test-Path -LiteralPath $DestinationDir)) {
        New-Item -ItemType Directory -Path $DestinationDir -Force | Out-Null
    }

    $skipped = New-Object 'System.Collections.Generic.List[string]'

    Get-ChildItem -LiteralPath $SourceDir -Recurse -Force | ForEach-Object {
        $src = $_.FullName
        $rel = $src.Substring($SourceDir.Length).TrimStart('\')
        if ([string]::IsNullOrWhiteSpace($rel)) { return }

        $dst = Join-Path $DestinationDir $rel

        if ($_.PSIsContainer) {
            if (-not (Test-Path -LiteralPath $dst)) {
                New-Item -ItemType Directory -Path $dst -Force | Out-Null
            }
            return
        }

        $dstParent = Split-Path -Parent $dst
        if (-not (Test-Path -LiteralPath $dstParent)) {
            New-Item -ItemType Directory -Path $dstParent -Force | Out-Null
        }

        try {
            Copy-Item -LiteralPath $src -Destination $dst -Force -ErrorAction Stop
        }
        catch {
            $copyErrorMessage = ""
            if ($_.Exception -and $_.Exception.Message) {
                $copyErrorMessage = [string]$_.Exception.Message
            }

            if ($copyErrorMessage -match "(?i)denied") {
                $skipped.Add($src) | Out-Null
                Write-Warning ("Skipped locked file during staging copy: " + $src)
                return
            }

            throw
        }
    }

    if ($skipped.Count -gt 0) {
        Write-Warning ("Staging copy completed with " + $skipped.Count + " skipped locked file(s).")
    }
}

function Copy-FileWithFallback {
    param(
        [Parameter(Mandatory = $true)][string]$SourcePath,
        [Parameter(Mandatory = $true)][string]$DestinationPath
    )

    $destParent = Split-Path -Parent $DestinationPath
    if (-not [string]::IsNullOrWhiteSpace($destParent) -and -not (Test-Path -LiteralPath $destParent)) {
        New-Item -ItemType Directory -Path $destParent -Force | Out-Null
    }

    try {
        Copy-Item -LiteralPath $SourcePath -Destination $DestinationPath -Force -ErrorAction Stop
        return
    }
    catch {
        # Some environments block Copy-Item for .exe files; fallback to cmd copy.
        $sourceExt = [System.IO.Path]::GetExtension($SourcePath)
        $destExt = [System.IO.Path]::GetExtension($DestinationPath)
        if ($sourceExt -ieq ".exe" -or $destExt -ieq ".exe") {
            $copyCmd = 'copy /Y "' + $SourcePath + '" "' + $DestinationPath + '"'
            cmd /c $copyCmd | Out-Null
            if ($LASTEXITCODE -eq 0 -and (Test-Path -LiteralPath $DestinationPath)) {
                return
            }
        }

        throw
    }
}

function Resolve-IsccPath {
    param([string]$explicitPath)

    if (-not [string]::IsNullOrWhiteSpace($explicitPath)) {
        if (Test-Path -LiteralPath $explicitPath) {
            return (Resolve-Path -LiteralPath $explicitPath).Path
        }
        throw "ISCC.exe not found at explicit path: $explicitPath"
    }

    $candidates = @(
        "C:\\Program Files (x86)\\Inno Setup 6\\ISCC.exe",
        "C:\\Program Files\\Inno Setup 6\\ISCC.exe"
    )

    foreach ($candidate in $candidates) {
        if (Test-Path -LiteralPath $candidate) {
            return $candidate
        }
    }

    throw "ISCC.exe not found. Install Inno Setup 6."
}

function Resolve-SourceDir {
    param([string]$explicitSourceDir)

    if (-not [string]::IsNullOrWhiteSpace($explicitSourceDir)) {
        if (-not (Test-Path -LiteralPath $explicitSourceDir)) {
            throw "SourceDir does not exist: $explicitSourceDir"
        }
        return (Resolve-Path -LiteralPath $explicitSourceDir).Path
    }

    $latest = Get-ChildItem -Path ".\\release" -Directory -Filter "CamboBIM_Deploy_Package_*" -ErrorAction SilentlyContinue |
        Sort-Object LastWriteTime -Descending |
        Select-Object -First 1

    if ($null -ne $latest) {
        return $latest.FullName
    }

    $generated = Join-Path $projectRoot "release\inno\_base_package"
    if (-not (Test-Path -LiteralPath $generated)) {
        New-Item -ItemType Directory -Path $generated -Force | Out-Null
    }

    $readmePath = Join-Path $generated "README_DEPLOY.txt"
    $readme = @(
        "MHNK / CamboBIM Revit Deploy Package",
        "",
        "This package is generated by scripts\build-inno-installer.ps1.",
        "The installer includes the selected Revit add-in runtime, add-in templates,",
        "and deploy tool for the target Revit year.",
        "",
        "Install as Administrator when deploying for all users."
    ) -join [Environment]::NewLine
    Set-Content -LiteralPath $readmePath -Value $readme -Encoding UTF8

    return $generated
}

function Resolve-RuntimeDir {
    param(
        [string]$runtimePath,
        [string]$year
    )

    $candidates = @()
    if (-not [string]::IsNullOrWhiteSpace($runtimePath)) {
        $candidates += $runtimePath
    }
    else {
        $candidates += Join-Path $projectRoot ("bin\Revit" + $year + "\x64\Release")
        $candidates += Join-Path $projectRoot ("bin\Revit" + $year + "\Release")
        $candidates += Join-Path $projectRoot ("bin\Revit" + $year + "\AnyCPU\Release")
    }

    $assemblyFileName = "CamboBIM.Revit" + $year + ".Addin.dll"
    $checked = New-Object 'System.Collections.Generic.List[string]'
    foreach ($candidate in $candidates) {
        if ([string]::IsNullOrWhiteSpace($candidate)) {
            continue
        }

        $checked.Add($candidate) | Out-Null
        if (-not (Test-Path -LiteralPath $candidate)) {
            continue
        }

        $resolved = (Resolve-Path -LiteralPath $candidate).Path
        $dllPath = Join-Path $resolved $assemblyFileName
        if (-not (Test-Path -LiteralPath $dllPath)) {
            continue
        }

        $dll = Get-Item -LiteralPath $dllPath
        if (-not $AllowSmallRuntime -and $dll.Length -lt 65536) {
            throw "Runtime DLL is too small and may be a design-time stub: $dllPath ($($dll.Length) bytes). Build a real Release add-in on a Revit API machine, or pass -AllowSmallRuntime only for dry-run packaging."
        }

        return $resolved
    }

    $checkedText = ($checked | ForEach-Object { " - $_" }) -join [Environment]::NewLine
    throw "RuntimeDir containing $assemblyFileName was not found. Paths checked:`n$checkedText"
}

$iscc = Resolve-IsccPath -explicitPath $IsccPath
$source = Resolve-SourceDir -explicitSourceDir $SourceDir
$script = (Resolve-Path -LiteralPath $ScriptPath).Path

$output = if ([System.IO.Path]::IsPathRooted($OutputDir)) {
    $OutputDir
}
else {
    [System.IO.Path]::GetFullPath((Join-Path (Get-Location).Path $OutputDir))
}

if (-not (Test-Path -LiteralPath $output)) {
    New-Item -Path $output -ItemType Directory | Out-Null
}

$effectiveSource = $source
if ($IncludeAddinRuntime) {
    $resolvedRuntime = Resolve-RuntimeDir -runtimePath $RuntimeDir -year $RevitYear
    $stageDir = Join-Path $output ("_source_with_runtime_" + (Get-Date -Format "yyyyMMdd_HHmmss"))

    New-Item -ItemType Directory -Path $stageDir | Out-Null
    Copy-DirectoryBestEffort -SourceDir $source -DestinationDir $stageDir
    Copy-DirectoryBestEffort -SourceDir $resolvedRuntime -DestinationDir $stageDir

    $currentAddinsDir = Join-Path $projectRoot "addins"
    if (Test-Path -LiteralPath $currentAddinsDir) {
        Copy-DirectoryBestEffort -SourceDir $currentAddinsDir -DestinationDir (Join-Path $stageDir "addins")
    }

    # Always prefer the latest deploy tools from current source tree.
    $latestDeployExe = Join-Path $projectRoot ("scripts\deploy-revit" + $RevitYear + "-addin.exe")
    if (-not (Test-Path -LiteralPath $latestDeployExe)) {
        $latestDeployExe = Join-Path $projectRoot "scripts\deploy-revit2024-addin.exe"
    }
    if (Test-Path -LiteralPath $latestDeployExe) {
        $yearDeployExeName = "deploy-revit" + $RevitYear + "-addin.exe"
        Copy-FileWithFallback -SourcePath $latestDeployExe -DestinationPath (Join-Path $stageDir $yearDeployExeName)
    }

    $latestDeployPs1 = Join-Path $projectRoot ("scripts\deploy-revit" + $RevitYear + "-addin.ps1")
    if (-not (Test-Path -LiteralPath $latestDeployPs1)) {
        $latestDeployPs1 = Join-Path $projectRoot "scripts\deploy-revit2024-addin.ps1"
    }
    if (Test-Path -LiteralPath $latestDeployPs1) {
        $yearDeployPs1Name = "deploy-revit" + $RevitYear + "-addin.ps1"
        Copy-FileWithFallback -SourcePath $latestDeployPs1 -DestinationPath (Join-Path $stageDir $yearDeployPs1Name)
    }

    $effectiveSource = $stageDir
}

Write-Host "Building Inno Setup installer..."
Write-Host "ISCC:"
Write-Host "  $iscc"
Write-Host "Script:"
Write-Host "  $script"
Write-Host "SourceDir:"
Write-Host "  $source"
Write-Host "RevitYear:"
Write-Host "  $RevitYear"
if ($IncludeAddinRuntime) {
    Write-Host "RuntimeDir:"
    Write-Host "  $resolvedRuntime"
    Write-Host "MergedSourceDir:"
    Write-Host "  $effectiveSource"
}
Write-Host "OutputDir:"
Write-Host "  $output"

$args = @(
    "/DSourceDir=$effectiveSource",
    "/DRevitYear=$RevitYear",
    "/DOutputDir=$output",
    $script
)

& $iscc @args
if ($LASTEXITCODE -ne 0) {
    throw "ISCC failed with exit code $LASTEXITCODE."
}

$setup = Get-ChildItem -Path $output -Filter "*.exe" |
    Where-Object { $_.Name -like "MHNK*.exe" -or $_.Name -like "CamboBIM*.exe" } |
    Sort-Object LastWriteTime -Descending |
    Select-Object -First 1

if ($null -eq $setup) {
    throw "Installer build finished but setup EXE was not found in: $output"
}

Write-Host ""
Write-Host "Installer created:"
Write-Host "  $($setup.FullName)"
