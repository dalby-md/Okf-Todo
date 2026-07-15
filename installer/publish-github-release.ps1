[CmdletBinding()]
param(
    [ValidatePattern('^\d+\.\d+\.\d+(\.\d+)?$')]
    [string]$Version = '0.1.0'
)

$ErrorActionPreference = 'Stop'

$repoRoot = Split-Path -Parent $PSScriptRoot
$tag = "v$Version"
$installerPath = Join-Path `
    $repoRoot `
    "artifacts\installer\Okf-Todo-$Version-win-x64-setup.exe"

if (-not (Test-Path -LiteralPath $installerPath -PathType Leaf)) {
    throw "Installer not found: $installerPath. Build it with .\installer\build-installer.ps1 -Version $Version"
}

if ($null -eq (Get-Command gh -ErrorAction SilentlyContinue)) {
    throw 'GitHub CLI (gh) was not found. Install it and authenticate with gh auth login.'
}

Push-Location $repoRoot
try {
    & gh release create `
        $tag `
        $installerPath `
        --title "OKF-Todo $Version" `
        --notes "Windows x64 installer for OKF-Todo $Version."

    if ($LASTEXITCODE -ne 0) {
        throw "GitHub release creation failed with exit code $LASTEXITCODE."
    }
}
finally {
    Pop-Location
}
