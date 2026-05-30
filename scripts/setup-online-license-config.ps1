param(
    [string]$ServerUrl,
    [string]$TargetPath,
    [switch]$SkipUrlTest,
    [switch]$OpenFolder
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Write-Info([string]$message) {
    Write-Host "[CamboBIM] $message" -ForegroundColor Cyan
}

function Write-WarnMsg([string]$message) {
    Write-Host "[CamboBIM] $message" -ForegroundColor Yellow
}

function Write-Ok([string]$message) {
    Write-Host "[CamboBIM] $message" -ForegroundColor Green
}

function Get-ProgramDataPath {
    if (-not [string]::IsNullOrWhiteSpace($env:ProgramData)) {
        return $env:ProgramData
    }

    return [Environment]::GetFolderPath([Environment+SpecialFolder]::CommonApplicationData)
}

function Read-ServerUrlInteractively {
    Write-Host ""
    Write-Host "Paste your Google Apps Script Web App URL (Web app URL, not Deployment ID)." -ForegroundColor White
    Write-Host "Example: https://script.google.com/macros/s/AKfy.../exec" -ForegroundColor DarkGray
    $inputUrl = Read-Host "Server URL"
    if ($null -eq $inputUrl) { return "" }
    return ($inputUrl.Trim().Trim('"').Trim("'"))
}

function Get-DefaultGoogleConfig([string]$url) {
    return [ordered]@{
        enabled                 = $true
        server_url              = $url
        product_code            = "CBIM_RVT2024_EXTENSION"
        activate_endpoint       = "?action=activate"
        heartbeat_endpoint      = "?action=heartbeat"
        release_endpoint        = "?action=release"
        change_password_endpoint= "?action=change_password"
        support_contact_email   = "support@cambobim.com"
        support_contact_phone   = "+855 69 901 004"
        support_contact_telegram= "@CamboBIMSupport"
        timeout_seconds         = 20
        heartbeat_seconds       = 300
    }
}

function Test-ServerUrl([string]$url) {
    try {
        Write-Info "Testing Web App URL..."
        $response = Invoke-WebRequest -Uri $url -Method Get -UseBasicParsing -TimeoutSec 15
        if (-not $response -or [string]::IsNullOrWhiteSpace($response.Content)) {
            Write-WarnMsg "URL responded but returned empty content."
            return $false
        }

        try {
            $json = $response.Content | ConvertFrom-Json
            if ($json -and $json.success -eq $true) {
                Write-Ok ("Web app test OK: " + ($json.message | Out-String).Trim())
                return $true
            }

            Write-WarnMsg "Web app responded but JSON 'success' is not true."
            return $false
        }
        catch {
            Write-WarnMsg "URL responded, but content is not JSON. Check that this is the Web app URL."
            return $false
        }
    }
    catch {
        Write-WarnMsg ("Web app test failed: " + $_.Exception.Message)
        return $false
    }
}

function Merge-WithExistingConfig([string]$path, [hashtable]$defaults, [string]$url) {
    if (-not (Test-Path -LiteralPath $path)) {
        return $defaults
    }

    try {
        $raw = Get-Content -LiteralPath $path -Raw
        if ([string]::IsNullOrWhiteSpace($raw)) {
            return $defaults
        }

        $existing = $raw | ConvertFrom-Json -ErrorAction Stop
        $merged = [ordered]@{}

        foreach ($key in $defaults.Keys) {
            $merged[$key] = $defaults[$key]
        }

        foreach ($prop in $existing.PSObject.Properties) {
            if (-not $prop) { continue }
            $name = [string]$prop.Name
            $value = $prop.Value
            if ([string]::IsNullOrWhiteSpace($name)) { continue }

            if ($name -eq "server_url") {
                $merged[$name] = $url
                continue
            }

            if ($merged.Contains($name)) {
                # Keep existing values for non-endpoint/support fields when present,
                # but force Google Apps Script endpoints to match this tool.
                if ($name -in @("activate_endpoint", "heartbeat_endpoint", "release_endpoint", "change_password_endpoint")) {
                    continue
                }

                if ($null -ne $value -and -not ([string]$value -eq "")) {
                    $merged[$name] = $value
                }
            }
            else {
                $merged[$name] = $value
            }
        }

        return $merged
    }
    catch {
        Write-WarnMsg "Existing config is invalid JSON. It will be replaced with a fresh config."
        return $defaults
    }
}

try {
    if ([string]::IsNullOrWhiteSpace($ServerUrl)) {
        $ServerUrl = Read-ServerUrlInteractively
    }

    if ([string]::IsNullOrWhiteSpace($ServerUrl)) {
        throw "Server URL is required."
    }

    $uri = $null
    if (-not [Uri]::TryCreate($ServerUrl, [UriKind]::Absolute, [ref]$uri)) {
        throw "Invalid URL format. Please paste the full Web app URL."
    }

    if ($uri.Scheme -ne "https") {
        throw "URL must use HTTPS."
    }

    if ($uri.Host -notlike "*script.google.com") {
        Write-WarnMsg "This URL is not a Google Apps Script URL. The tool will still write the config."
    }

    if ([string]::IsNullOrWhiteSpace($TargetPath)) {
        $programData = Get-ProgramDataPath
        if ([string]::IsNullOrWhiteSpace($programData)) {
            throw "Could not resolve ProgramData folder."
        }

        $targetDir = Join-Path $programData "CamboBIM"
        $targetPath = Join-Path $targetDir "online-license.config.json"
    }
    else {
        $targetPath = [System.IO.Path]::GetFullPath($TargetPath)
        $targetDir = Split-Path -Path $targetPath -Parent
    }

    if (-not (Test-Path -LiteralPath $targetDir)) {
        New-Item -ItemType Directory -Path $targetDir -Force | Out-Null
    }

    if (Test-Path -LiteralPath $targetPath) {
        $stamp = Get-Date -Format "yyyyMMdd_HHmmss"
        $backupPath = Join-Path $targetDir ("online-license.config.backup_{0}.json" -f $stamp)
        Copy-Item -LiteralPath $targetPath -Destination $backupPath -Force
        Write-Info ("Backed up existing config: " + $backupPath)
    }

    $defaults = Get-DefaultGoogleConfig -url $ServerUrl
    $config = Merge-WithExistingConfig -path $targetPath -defaults $defaults -url $ServerUrl

    $json = $config | ConvertTo-Json -Depth 10
    [System.IO.File]::WriteAllText($targetPath, $json, [System.Text.UTF8Encoding]::new($false))

    Write-Ok ("Config written: " + $targetPath)
    Write-Host ""
    Write-Host "Next steps for the user:" -ForegroundColor White
    Write-Host "1. Open Revit 2024" -ForegroundColor Gray
    Write-Host "2. Open CamboBIM add-in" -ForegroundColor Gray
    Write-Host "3. Login with the User Name and temporary Password you provided" -ForegroundColor Gray
    Write-Host "4. Change password after login (recommended)" -ForegroundColor Gray

    if (-not $SkipUrlTest) {
        [void](Test-ServerUrl -url $ServerUrl)
    }

    if ($OpenFolder) {
        Start-Process explorer.exe $targetDir | Out-Null
    }

    Write-Host ""
    Write-Host "Current server_url:" -ForegroundColor White
    Write-Host $ServerUrl -ForegroundColor Gray
}
catch {
    Write-Host ""
    Write-Host "[CamboBIM] Setup failed: $($_.Exception.Message)" -ForegroundColor Red
    exit 1
}
