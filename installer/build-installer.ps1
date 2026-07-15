[CmdletBinding()]
param(
    [ValidatePattern('^\d+\.\d+\.\d+(\.\d+)?$')]
    [string]$Version = '0.1.0',

    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    [ValidateSet('win-x64')]
    [string]$RuntimeIdentifier = 'win-x64',

    [string]$SignToolPath,

    [string]$CertificateThumbprint,

    [string]$TimestampUrl = 'http://timestamp.digicert.com',

    [switch]$SkipInstallerCompile
)

$ErrorActionPreference = 'Stop'
$ProgressPreference = 'SilentlyContinue'

$repoRoot = Split-Path -Parent $PSScriptRoot
$artifactRoot = Join-Path $repoRoot 'artifacts\installer'
$publishRoot = Join-Path $artifactRoot 'publish'
$guiPublish = Join-Path $publishRoot 'gui'
$mcpPublish = Join-Path $publishRoot 'mcp'
$stagingRoot = Join-Path $artifactRoot 'staging'
$coreStaging = Join-Path $stagingRoot 'core'
$mcpStaging = Join-Path $stagingRoot 'mcp'
$okfStaging = Join-Path $stagingRoot 'okf\todo-database'
$integrationStaging = Join-Path $stagingRoot 'integration'

function Reset-Directory {
    param([Parameter(Mandatory)][string]$Path)

    if (Test-Path -LiteralPath $Path) {
        Remove-Item -LiteralPath $Path -Recurse -Force
    }

    New-Item -ItemType Directory -Path $Path -Force | Out-Null
}

function Copy-DirectoryContents {
    param(
        [Parameter(Mandatory)][string]$Source,
        [Parameter(Mandatory)][string]$Destination
    )

    New-Item -ItemType Directory -Path $Destination -Force | Out-Null
    Get-ChildItem -LiteralPath $Source -Force | Copy-Item -Destination $Destination -Recurse -Force
}

function Invoke-Publish {
    param(
        [Parameter(Mandatory)][string]$Project,
        [Parameter(Mandatory)][string]$Output
    )

    & dotnet publish $Project `
        -c $Configuration `
        -r $RuntimeIdentifier `
        --self-contained true `
        -p:DebugType=None `
        -p:DebugSymbols=false `
        -o $Output

    if ($LASTEXITCODE -ne 0) {
        throw "dotnet publish failed for $Project with exit code $LASTEXITCODE."
    }
}

function Assert-FileExists {
    param([Parameter(Mandatory)][string]$Path)

    if (-not (Test-Path -LiteralPath $Path -PathType Leaf)) {
        throw "Required installer file is missing: $Path"
    }
}

function Invoke-SignFile {
    param([Parameter(Mandatory)][string]$Path)

    if ([string]::IsNullOrWhiteSpace($SignToolPath)) {
        return
    }

    & $SignToolPath sign `
        /sha1 $CertificateThumbprint `
        /fd SHA256 `
        /tr $TimestampUrl `
        /td SHA256 `
        $Path

    if ($LASTEXITCODE -ne 0) {
        throw "Authenticode signing failed for $Path with exit code $LASTEXITCODE."
    }
}

function Find-InnoCompiler {
    if (-not [string]::IsNullOrWhiteSpace($env:ISCC_PATH) -and
        (Test-Path -LiteralPath $env:ISCC_PATH -PathType Leaf)) {
        return $env:ISCC_PATH
    }

    $candidates = @(
        (Join-Path $repoRoot 'artifacts\tools\innosetup7\ISCC.exe'),
        (Join-Path $env:LOCALAPPDATA 'Programs\Inno Setup 7\ISCC.exe'),
        (Join-Path $env:ProgramFiles 'Inno Setup 7\ISCC.exe'),
        (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 7\ISCC.exe'),
        (Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'),
        (Join-Path $env:ProgramFiles 'Inno Setup 6\ISCC.exe')
    ) | Where-Object { -not [string]::IsNullOrWhiteSpace($_) }

    foreach ($candidate in $candidates) {
        if (Test-Path -LiteralPath $candidate -PathType Leaf) {
            return $candidate
        }
    }

    $command = Get-Command ISCC.exe -ErrorAction SilentlyContinue
    if ($null -ne $command) {
        return $command.Source
    }

    throw 'Inno Setup 7 or 6 was not found. Install it or set ISCC_PATH to the full path of ISCC.exe. Use -SkipInstallerCompile to build and validate staging only.'
}

Push-Location $repoRoot
try {
    if ([string]::IsNullOrWhiteSpace($SignToolPath) -ne [string]::IsNullOrWhiteSpace($CertificateThumbprint)) {
        throw 'SignToolPath and CertificateThumbprint must be supplied together.'
    }

    if (-not [string]::IsNullOrWhiteSpace($SignToolPath)) {
        Assert-FileExists -Path $SignToolPath
    }

    Reset-Directory -Path $artifactRoot
    New-Item -ItemType Directory -Path $publishRoot -Force | Out-Null

    Write-Host "Publishing OKF-Todo GUI for $RuntimeIdentifier..."
    Invoke-Publish -Project (Join-Path $repoRoot 'Okf-Todo\Okf-Todo.csproj') -Output $guiPublish

    Write-Host "Publishing OKF-Todo MCP server for $RuntimeIdentifier..."
    Invoke-Publish -Project (Join-Path $repoRoot 'Okf-Todo.Mcp\Okf-Todo.Mcp.csproj') -Output $mcpPublish

    Reset-Directory -Path $stagingRoot
    Copy-DirectoryContents -Source $guiPublish -Destination $coreStaging
    New-Item -ItemType Directory -Path $mcpStaging -Force | Out-Null

    # Keep the MCP runtime isolated. The MCP SDK currently resolves newer
    # Microsoft.Extensions/System.* assemblies than the Photino GUI, so merging
    # both self-contained publishes into one directory is not safe.
    Copy-DirectoryContents -Source $mcpPublish -Destination $mcpStaging

    # The MCP project references the GUI project for shared application services,
    # which causes the static frontend to be copied into its publish output. The
    # stdio server does not serve these assets.
    $mcpWebRoot = Join-Path $mcpStaging 'wwwroot'
    if (Test-Path -LiteralPath $mcpWebRoot) {
        Remove-Item -LiteralPath $mcpWebRoot -Recurse -Force
    }

    Copy-DirectoryContents `
        -Source (Join-Path $repoRoot 'docs\okf\todo-database') `
        -Destination $okfStaging

    New-Item -ItemType Directory -Path $integrationStaging -Force | Out-Null
    Copy-Item `
        -LiteralPath (Join-Path $PSScriptRoot 'installed-readme.md') `
        -Destination (Join-Path $integrationStaging 'README.md') `
        -Force
    Assert-FileExists -Path (Join-Path $coreStaging 'Okf-Todo.exe')
    Assert-FileExists -Path (Join-Path $coreStaging 'lookup-seed.json')
    Assert-FileExists -Path (Join-Path $coreStaging 'wwwroot\index.html')
    Assert-FileExists -Path (Join-Path $mcpStaging 'Okf-Todo.Mcp.exe')
    Assert-FileExists -Path (Join-Path $mcpStaging 'lookup-seed.json')
    Assert-FileExists -Path (Join-Path $okfStaging 'index.md')

    if (Test-Path -LiteralPath (Join-Path $mcpStaging 'wwwroot')) {
        throw 'MCP staging unexpectedly contains wwwroot after frontend pruning.'
    }

    Invoke-SignFile -Path (Join-Path $coreStaging 'Okf-Todo.exe')
    Invoke-SignFile -Path (Join-Path $mcpStaging 'Okf-Todo.Mcp.exe')

    Write-Host "Staging ready at $stagingRoot"
    Write-Host 'The optional MCP component has an isolated self-contained runtime.'

    if ($SkipInstallerCompile) {
        Write-Host 'Skipping Inno Setup compilation as requested.'
        return
    }

    $iscc = Find-InnoCompiler
    $installerScript = Join-Path $PSScriptRoot 'Okf-Todo.iss'
    Write-Host "Compiling installer with $iscc..."
    & $iscc "/DAppVersion=$Version" $installerScript
    if ($LASTEXITCODE -ne 0) {
        throw "Inno Setup compilation failed with exit code $LASTEXITCODE."
    }

    $installerPath = Join-Path $artifactRoot "Okf-Todo-$Version-win-x64-setup.exe"
    Assert-FileExists -Path $installerPath
    Invoke-SignFile -Path $installerPath
    Write-Host "Installer created at $installerPath"
}
finally {
    Pop-Location
}
