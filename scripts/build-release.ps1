[CmdletBinding()]
param(
    [ValidatePattern('^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$')]
    [string] $Version = '0.22.1',

    [string] $OutputRoot = '',

    [string] $SignParams = ''
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repositoryRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($OutputRoot)) {
    $OutputRoot = Join-Path $repositoryRoot 'artifacts\release-local'
}
elseif (-not [System.IO.Path]::IsPathRooted($OutputRoot)) {
    $OutputRoot = Join-Path $repositoryRoot $OutputRoot
}

$OutputRoot = [System.IO.Path]::GetFullPath($OutputRoot)
$publishDirectory = Join-Path $OutputRoot 'publish\win-x64'
$releaseDirectory = Join-Path $OutputRoot 'releases'
$appProject = Join-Path $repositoryRoot 'src\LiveAudioBoard.App\LiveAudioBoard.App.csproj'
$solution = Join-Path $repositoryRoot 'LiveAudioBoard.sln'
$iconPath = Join-Path $repositoryRoot 'assets\branding\app-icon.ico'

function Invoke-Checked {
    param(
        [Parameter(Mandatory)] [string] $Command,
        [Parameter(Mandatory)] [string[]] $Arguments
    )

    & $Command @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "Command failed with exit code ${LASTEXITCODE}: $Command"
    }
}

foreach ($directory in @($publishDirectory, $releaseDirectory)) {
    if ([System.IO.Directory]::Exists($directory)) {
        [System.IO.Directory]::Delete($directory, $true)
    }

    [System.IO.Directory]::CreateDirectory($directory) | Out-Null
}

Push-Location $repositoryRoot
try {
    & (Join-Path $PSScriptRoot 'verify-release-metadata.ps1') -Version $Version
    Invoke-Checked dotnet @('tool', 'restore')
    Invoke-Checked dotnet @('restore', $solution)
    Invoke-Checked dotnet @(
        'test', $solution,
        '--configuration', 'Release',
        '--no-restore'
    )
    Invoke-Checked dotnet @(
        'publish', $appProject,
        '--configuration', 'Release',
        '--runtime', 'win-x64',
        '--self-contained', 'true',
        '--output', $publishDirectory,
        '-p:PublishProfile=win-x64',
        "-p:Version=$Version"
    )
    $velopackArguments = @(
        'vpk', 'pack',
        '--outputDir', $releaseDirectory,
        '--runtime', 'win-x64',
        '--packId', 'LiveAudioBoard',
        '--packVersion', $Version,
        '--packDir', $publishDirectory,
        '--packAuthors', 'LiveAudioBoard Contributors',
        '--packTitle', 'LiveAudioBoard',
        '--mainExe', 'LiveAudioBoard.exe',
        '--icon', $iconPath,
        '--msi',
        '--instLocation', 'PerUser'
    )
    if (-not [string]::IsNullOrWhiteSpace($SignParams)) {
        $velopackArguments += @('--signParams', $SignParams)
    }

    Invoke-Checked dotnet $velopackArguments

    $portableArchive = Join-Path $releaseDirectory "LiveAudioBoard-$Version-win-x64-portable.zip"
    Compress-Archive -Path (Join-Path $publishDirectory '*') -DestinationPath $portableArchive
    foreach ($requiredDocument in @('LICENSE.txt', 'THIRD_PARTY_NOTICES.md')) {
        $publishedDocument = Join-Path $publishDirectory $requiredDocument
        if (-not (Test-Path -LiteralPath $publishedDocument -PathType Leaf)) {
            throw "Published application is missing $requiredDocument."
        }
    }

    $checksumPath = Join-Path $releaseDirectory 'SHA256SUMS.txt'
    $checksumLines = Get-ChildItem -LiteralPath $releaseDirectory -File |
        Where-Object { $_.Name -ne 'SHA256SUMS.txt' } |
        Sort-Object Name |
        ForEach-Object {
            $hash = (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant()
            "$hash  $($_.Name)"
        }
    [System.IO.File]::WriteAllLines(
        $checksumPath,
        $checksumLines,
        [System.Text.UTF8Encoding]::new($false))
    Write-Host "Release artifacts created in $releaseDirectory"
}
finally {
    Pop-Location
}
