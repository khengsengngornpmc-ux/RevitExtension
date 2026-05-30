param(
    [ValidateSet("Debug", "Release")]
    [string]$Configuration = "Debug",
    [ValidateSet("AnyCPU", "x64")]
    [string]$Platform = "AnyCPU",
    [ValidatePattern("^\d{4}$")]
    [string]$RevitYear = "2024",
    [string]$RevitExePath = "",
    [string]$ProjectPath = "",
    [string]$DeployScriptPath = "",
    [switch]$NoBuild,
    [switch]$NoLaunch,
    [switch]$ForceClose
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"
try {
    Set-ExecutionPolicy -Scope Process Bypass -Force -ErrorAction Stop
}
catch {
    # Best effort only. The script can still run if policy is already permissive.
}

function Resolve-DefaultRevitExePath {
    param(
        [Parameter(Mandatory = $true)][string]$Year,
        [AllowEmptyString()]
        [string]$ExplicitPath
    )

    if (-not [string]::IsNullOrWhiteSpace($ExplicitPath)) {
        return $ExplicitPath
    }

    return "C:\Program Files\Autodesk\Revit " + $Year + "\Revit.exe"
}

function Resolve-ProjectPath {
    param(
        [Parameter(Mandatory = $true)][string]$ProjectRoot,
        [Parameter(Mandatory = $true)][string]$Year,
        [AllowEmptyString()]
        [string]$ExplicitPath
    )

    if (-not [string]::IsNullOrWhiteSpace($ExplicitPath)) {
        if (-not (Test-Path -LiteralPath $ExplicitPath)) {
            throw "Project file not found: $ExplicitPath"
        }

        return (Resolve-Path -LiteralPath $ExplicitPath).Path
    }

    $candidates = @(
        (Join-Path $ProjectRoot ("CamboBIM.Revit" + $Year + ".Addin.csproj")),
        (Join-Path $ProjectRoot "CamboBIM.Revit2024.Addin.csproj")
    )

    $wildcards = @(Get-ChildItem -Path $ProjectRoot -Filter "CamboBIM.Revit*.Addin.csproj" -File -ErrorAction SilentlyContinue |
        Sort-Object Name |
        ForEach-Object { $_.FullName })
    $candidates += $wildcards
    $candidates = @($candidates | Select-Object -Unique)

    foreach ($candidate in $candidates) {
        if (Test-Path -LiteralPath $candidate) {
            return (Resolve-Path -LiteralPath $candidate).Path
        }
    }

    $checked = ($candidates | ForEach-Object { " - $_" }) -join [Environment]::NewLine
    throw "Project file not found for RevitYear '$Year'. Paths checked:`n$checked"
}

function Resolve-DeployScriptPath {
    param(
        [Parameter(Mandatory = $true)][string]$ProjectRoot,
        [Parameter(Mandatory = $true)][string]$Year,
        [AllowEmptyString()]
        [string]$ExplicitPath
    )

    if (-not [string]::IsNullOrWhiteSpace($ExplicitPath)) {
        if (-not (Test-Path -LiteralPath $ExplicitPath)) {
            throw "Deploy script not found: $ExplicitPath"
        }

        return (Resolve-Path -LiteralPath $ExplicitPath).Path
    }

    $scriptsDir = Join-Path $ProjectRoot "scripts"
    $candidates = @(
        (Join-Path $scriptsDir ("deploy-revit" + $Year + "-addin.ps1")),
        (Join-Path $scriptsDir "deploy-revit2024-addin.ps1")
    )

    $wildcards = @(Get-ChildItem -Path $scriptsDir -Filter "deploy-revit*-addin.ps1" -File -ErrorAction SilentlyContinue |
        Sort-Object Name |
        ForEach-Object { $_.FullName })
    $candidates += $wildcards
    $candidates = @($candidates | Select-Object -Unique)

    foreach ($candidate in $candidates) {
        if (Test-Path -LiteralPath $candidate) {
            return (Resolve-Path -LiteralPath $candidate).Path
        }
    }

    $checked = ($candidates | ForEach-Object { " - $_" }) -join [Environment]::NewLine
    throw "Deploy script not found for RevitYear '$Year'. Paths checked:`n$checked"
}

$projectRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Resolve-ProjectPath -ProjectRoot $projectRoot -Year $RevitYear -ExplicitPath $ProjectPath
$deployScriptPath = Resolve-DeployScriptPath -ProjectRoot $projectRoot -Year $RevitYear -ExplicitPath $DeployScriptPath
$RevitExePath = Resolve-DefaultRevitExePath -Year $RevitYear -ExplicitPath $RevitExePath

function Get-MSBuildPath {
    $vswherePath = Join-Path ${env:ProgramFiles(x86)} "Microsoft Visual Studio\Installer\vswhere.exe"
    if (Test-Path $vswherePath) {
        $found = & $vswherePath -latest -products * -requires Microsoft.Component.MSBuild -find "MSBuild\**\Bin\MSBuild.exe" |
            Select-Object -First 1
        if (-not [string]::IsNullOrWhiteSpace($found) -and (Test-Path $found)) {
            return $found
        }
    }

    $candidates = @(
        "C:\Program Files\Microsoft Visual Studio\18\Community\MSBuild\Current\Bin\MSBuild.exe",
        "C:\Program Files\Microsoft Visual Studio\18\Professional\MSBuild\Current\Bin\MSBuild.exe",
        "C:\Program Files\Microsoft Visual Studio\18\Enterprise\MSBuild\Current\Bin\MSBuild.exe",
        "C:\Program Files\Microsoft Visual Studio\17\Community\MSBuild\Current\Bin\MSBuild.exe",
        "C:\Program Files\Microsoft Visual Studio\17\Professional\MSBuild\Current\Bin\MSBuild.exe",
        "C:\Program Files\Microsoft Visual Studio\17\Enterprise\MSBuild\Current\Bin\MSBuild.exe"
    )

    foreach ($candidate in $candidates) {
        if (Test-Path $candidate) {
            return $candidate
        }
    }

    throw "MSBuild.exe not found. Install Visual Studio Build Tools or Visual Studio."
}

function Get-RevitProcessesForExePath {
    param(
        [Parameter(Mandatory = $true)][string]$TargetExePath
    )

    $allRevitProcesses = @(Get-Process Revit -ErrorAction SilentlyContinue)
    if ($allRevitProcesses.Count -eq 0) {
        return @()
    }

    if ([string]::IsNullOrWhiteSpace($TargetExePath)) {
        return $allRevitProcesses
    }

    $targetPath = $TargetExePath
    if (Test-Path -LiteralPath $TargetExePath) {
        $targetPath = (Resolve-Path -LiteralPath $TargetExePath).Path
    }

    $cimMatches = @(Get-CimInstance Win32_Process -Filter "Name = 'Revit.exe'" -ErrorAction SilentlyContinue |
        Where-Object {
            -not [string]::IsNullOrWhiteSpace($_.ExecutablePath) -and
            $_.ExecutablePath.Equals($targetPath, [StringComparison]::OrdinalIgnoreCase)
        })
    if ($cimMatches.Count -gt 0) {
        $matchIds = @($cimMatches | ForEach-Object { [int]$_.ProcessId })
        return @($allRevitProcesses | Where-Object { $matchIds -contains $_.Id })
    }

    $pathMatches = @()
    foreach ($process in $allRevitProcesses) {
        $processPath = $null
        try {
            $processPath = $process.Path
        }
        catch {
            $processPath = $null
        }

        if (-not [string]::IsNullOrWhiteSpace($processPath) -and
            $processPath.Equals($targetPath, [StringComparison]::OrdinalIgnoreCase)) {
            $pathMatches += $process
        }
    }

    return $pathMatches
}

function Stop-RevitIfRunning {
    param(
        [Parameter(Mandatory = $true)][string]$TargetExePath
    )

    $processes = @(Get-RevitProcessesForExePath -TargetExePath $TargetExePath)
    if ($processes.Count -eq 0) {
        return
    }

    Write-Host "Closing Revit..."
    foreach ($process in $processes) {
        [void]$process.CloseMainWindow()
    }

    $deadline = (Get-Date).AddSeconds(20)
    while (@(Get-RevitProcessesForExePath -TargetExePath $TargetExePath).Count -gt 0 -and (Get-Date) -lt $deadline) {
        Start-Sleep -Milliseconds 500
    }

    $remaining = @(Get-RevitProcessesForExePath -TargetExePath $TargetExePath)
    if ($remaining.Count -eq 0) {
        return
    }

    if ($ForceClose) {
        Write-Host "Revit is still running. Force closing..."
        $remaining | ForEach-Object { Stop-Process -Id $_.Id -Force }
        Start-Sleep -Seconds 1
        return
    }

    throw "Target Revit instance is still running. Close it manually or run this script with -ForceClose."
}

Stop-RevitIfRunning -TargetExePath $RevitExePath

if (-not $NoBuild) {
    $msbuildPath = Get-MSBuildPath
    Write-Host "Revit year:"
    Write-Host "  $RevitYear"
    Write-Host "Project:"
    Write-Host "  $projectPath"
    Write-Host "Deploy script:"
    Write-Host "  $deployScriptPath"
    Write-Host "Building with:"
    Write-Host "  $msbuildPath"
    Write-Host "Configuration/Platform:"
    Write-Host "  $Configuration | $Platform"

    & $msbuildPath $projectPath /restore /t:Rebuild /p:Configuration=$Configuration /p:Platform=$Platform /p:DisableRevitDeploy=true /v:minimal
    if ($LASTEXITCODE -ne 0) {
        throw "Rebuild failed with exit code $LASTEXITCODE."
    }
}

Write-Host "Deploying Revit manifest..."
& $deployScriptPath -Configuration $Configuration -Platform $Platform -RevitYear $RevitYear

if (-not $NoLaunch) {
    if (-not (Test-Path $RevitExePath)) {
        throw "Revit executable not found: $RevitExePath"
    }

    $revitWorkingDir = Split-Path -Parent $RevitExePath
    Write-Host "Starting Revit..."
    Start-Process -FilePath $RevitExePath -WorkingDirectory $revitWorkingDir
}

Write-Host ""
Write-Host "Reload finished."
