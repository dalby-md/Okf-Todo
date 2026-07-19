[CmdletBinding(SupportsShouldProcess)]
param(
    [string]$Tag,

    [string]$SignToolPath,

    [string]$CertificateThumbprint,

    [string]$TimestampUrl = 'http://timestamp.digicert.com'
)

$ErrorActionPreference = 'Stop'
$tagPattern = '^v(?<major>\d+)\.(?<minor>\d+)\.(?<patch>\d+)-alpha$'
$repoRoot = Split-Path -Parent $PSScriptRoot
$buildScript = Join-Path $PSScriptRoot 'build-installer.ps1'
$artifactRoot = Join-Path $repoRoot 'artifacts\installer'

function Assert-GitHubCli {
    if ($null -eq (Get-Command gh -ErrorAction SilentlyContinue)) {
        throw 'GitHub CLI (gh) was not found. Install it and authenticate with gh auth login.'
    }
}

function Get-GitHubReleaseTags {
    $releaseTags = @(& gh release list --limit 1000 --json tagName --jq '.[].tagName')
    if ($LASTEXITCODE -ne 0) {
        throw "Could not read GitHub release tags. GitHub CLI exited with code $LASTEXITCODE."
    }

    return @(
        $releaseTags |
            ForEach-Object { $_.Trim() } |
            Where-Object { -not [string]::IsNullOrWhiteSpace($_) }
    )
}

function Get-NextAlphaTag {
    param([string[]]$ReleaseTags)

    $alphaVersions = foreach ($releaseTag in $ReleaseTags) {
        $match = [regex]::Match($releaseTag.Trim(), $tagPattern)
        if ($match.Success) {
            [pscustomobject]@{
                Major = [int]$match.Groups['major'].Value
                Minor = [int]$match.Groups['minor'].Value
                Patch = [int]$match.Groups['patch'].Value
            }
        }
    }

    $latestAlpha = $alphaVersions |
        Sort-Object Major, Minor, Patch -Descending |
        Select-Object -First 1

    if ($null -eq $latestAlpha) {
        return 'v0.1.0-alpha'
    }

    return "v$($latestAlpha.Major).$($latestAlpha.Minor).$($latestAlpha.Patch + 1)-alpha"
}

if (-not (Test-Path -LiteralPath $buildScript -PathType Leaf)) {
    throw "Installer build script not found: $buildScript"
}

Push-Location $repoRoot
try {
    $knownReleaseTags = $null

    if ([string]::IsNullOrWhiteSpace($Tag)) {
        Assert-GitHubCli
        $knownReleaseTags = @(Get-GitHubReleaseTags)
        $Tag = Get-NextAlphaTag -ReleaseTags $knownReleaseTags
    }
    else {
        $Tag = $Tag.Trim()
    }

    $tagMatch = [regex]::Match($Tag, $tagPattern)
    if (-not $tagMatch.Success) {
        throw "Invalid release tag '$Tag'. Expected a tag such as v0.1.5-alpha."
    }

    $major = [int]$tagMatch.Groups['major'].Value
    $minor = [int]$tagMatch.Groups['minor'].Value
    $patch = [int]$tagMatch.Groups['patch'].Value
    $version = "$major.$minor.$patch"
    $stableVersion = "$major.$minor"
    $versionedInstallerPath = Join-Path `
        $artifactRoot `
        "Okf-Todo-$version-win-x64-setup.exe"
    $stableAssetName = "Okf-Todo-$stableVersion-win-x64-setup.exe"
    $stableAssetPath = Join-Path $artifactRoot $stableAssetName
    $stableInstallerUrl = `
        "https://github.com/dalby-md/OKF-Todo/releases/latest/download/$stableAssetName"

    Write-Output "Release tag: $Tag"
    Write-Output "Installer version: $version"
    Write-Output "Stable asset name: $stableAssetName"

    if (-not $PSCmdlet.ShouldProcess(
        $Tag,
        "Build installer $version and publish a new GitHub release as latest")) {
        Write-Output "Stable installer URL: $stableInstallerUrl"
        return
    }

    Assert-GitHubCli

    if ($null -eq $knownReleaseTags) {
        $knownReleaseTags = @(Get-GitHubReleaseTags)
    }

    if ($knownReleaseTags -contains $Tag) {
        throw "GitHub release '$Tag' already exists. Choose a new tag."
    }

    $buildParameters = @{
        Version = $version
    }
    if (-not [string]::IsNullOrWhiteSpace($SignToolPath)) {
        $buildParameters.SignToolPath = $SignToolPath
    }
    if (-not [string]::IsNullOrWhiteSpace($CertificateThumbprint)) {
        $buildParameters.CertificateThumbprint = $CertificateThumbprint
    }
    if (-not [string]::IsNullOrWhiteSpace($TimestampUrl)) {
        $buildParameters.TimestampUrl = $TimestampUrl
    }

    & $buildScript @buildParameters
    if ($LASTEXITCODE -ne 0) {
        throw "Installer build failed with exit code $LASTEXITCODE."
    }

    if (-not (Test-Path -LiteralPath $versionedInstallerPath -PathType Leaf)) {
        throw "Installer not found after build: $versionedInstallerPath"
    }

    Copy-Item `
        -LiteralPath $versionedInstallerPath `
        -Destination $stableAssetPath `
        -Force

    & gh release create `
        $Tag `
        $stableAssetPath `
        --title "OKF-Todo $version alpha" `
        --notes 'Windows installer.' `
        --latest

    if ($LASTEXITCODE -ne 0) {
        throw "GitHub release creation failed with exit code $LASTEXITCODE."
    }

    Write-Output "Release $Tag created."
    Write-Output 'Versioned installer:'
    Write-Output $versionedInstallerPath
    Write-Output 'Stable release asset:'
    Write-Output $stableAssetPath
    Write-Output 'Stable installer URL:'
    Write-Output $stableInstallerUrl
}
finally {
    Pop-Location
}
