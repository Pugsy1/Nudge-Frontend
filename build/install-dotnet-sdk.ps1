<#
.SYNOPSIS
    Installs the .NET 10 SDK to a chosen directory (default D:\dotnet) and wires up the
    environment variables so `dotnet` resolves to it.

.DESCRIPTION
    Nudge targets .NET 10. This machine keeps development tooling off the C: drive, so the SDK
    is installed side-by-side rather than into C:\Program Files\dotnet.

    This wraps the official Microsoft installer script from https://dot.net/v1/dotnet-install.ps1.
    That script is Microsoft's supported way to install a .NET SDK to a custom location; it needs
    no administrator rights, and the whole install is undone by deleting the target folder and
    the two environment variables.

    Nothing is written to C: except a temporary copy of Microsoft's installer script in %TEMP%,
    which is deleted afterwards.

.PARAMETER InstallDir
    Where the SDK goes. Default D:\dotnet.

.PARAMETER Channel
    Which .NET channel to install. Default 10.0.

.EXAMPLE
    powershell -NoProfile -ExecutionPolicy Bypass -File .\build\install-dotnet-sdk.ps1
#>
[CmdletBinding()]
param(
    [string] $InstallDir = 'D:\dotnet',
    [string] $Channel = '10.0'
)

$ErrorActionPreference = 'Stop'

Write-Host ''
Write-Host '=== Nudge: .NET SDK setup ===' -ForegroundColor Cyan
Write-Host ("Target directory : {0}" -f $InstallDir)
Write-Host ("Channel          : {0}" -f $Channel)
Write-Host ''

# --- 1. Make sure the target drive exists before downloading anything -------------------------
$targetRoot = [System.IO.Path]::GetPathRoot($InstallDir)
if (-not (Test-Path -LiteralPath $targetRoot)) {
    throw "The drive '$targetRoot' does not exist. Pass a different -InstallDir."
}

if (-not (Test-Path -LiteralPath $InstallDir)) {
    New-Item -ItemType Directory -Path $InstallDir -Force | Out-Null
    Write-Host "Created $InstallDir"
}

# --- 2. Fetch Microsoft's official installer script -------------------------------------------
$scriptPath = Join-Path $env:TEMP 'dotnet-install.ps1'
Write-Host 'Downloading Microsoft installer script from https://dot.net/v1/dotnet-install.ps1 ...'
Invoke-WebRequest -Uri 'https://dot.net/v1/dotnet-install.ps1' -OutFile $scriptPath -UseBasicParsing
Write-Host ("Downloaded to {0} ({1:N0} bytes)" -f $scriptPath, (Get-Item $scriptPath).Length)
Write-Host ''

try {
    # --- 3. Install the SDK ------------------------------------------------------------------
    Write-Host 'Installing the SDK. This downloads roughly 200-300 MB and takes a few minutes.'
    & $scriptPath -Channel $Channel -InstallDir $InstallDir
    Write-Host ''
}
finally {
    Remove-Item -LiteralPath $scriptPath -Force -ErrorAction SilentlyContinue
}

# --- 4. Verify before touching the environment -------------------------------------------------
$dotnetExe = Join-Path $InstallDir 'dotnet.exe'
if (-not (Test-Path -LiteralPath $dotnetExe)) {
    throw "Install appears to have failed: $dotnetExe was not created."
}

$sdks = & $dotnetExe --list-sdks
if (-not $sdks) {
    throw "Install appears to have failed: '$dotnetExe --list-sdks' reported no SDKs."
}

Write-Host 'SDKs now present:' -ForegroundColor Green
$sdks | ForEach-Object { Write-Host "  $_" }
Write-Host ''

# --- 5. Point the machine at it (user scope, no admin needed) ----------------------------------
[Environment]::SetEnvironmentVariable('DOTNET_ROOT', $InstallDir, 'User')
Write-Host "Set DOTNET_ROOT = $InstallDir (user scope)"

$userPath = [Environment]::GetEnvironmentVariable('Path', 'User')
if ([string]::IsNullOrEmpty($userPath)) { $userPath = '' }

$alreadyOnPath = $userPath.Split(';') | Where-Object { $_.TrimEnd('\') -ieq $InstallDir.TrimEnd('\') }
if ($alreadyOnPath) {
    Write-Host "PATH already contains $InstallDir"
}
else {
    # Prepended so this SDK wins over any other dotnet.exe already on the machine.
    [Environment]::SetEnvironmentVariable('Path', "$InstallDir;$userPath", 'User')
    Write-Host "Prepended $InstallDir to your user PATH"
}

Write-Host ''
Write-Host '=== Done ===' -ForegroundColor Cyan
Write-Host 'Close this window, open a NEW terminal, then run:  dotnet --list-sdks'
Write-Host 'You should see a version starting with 10.'
Write-Host ''
