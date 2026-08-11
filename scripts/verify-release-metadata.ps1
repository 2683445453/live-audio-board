[CmdletBinding()]
param(
    [ValidatePattern('^\d+\.\d+\.\d+(?:-[0-9A-Za-z.-]+)?$')]
    [string] $Version = ''
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repositoryRoot = Split-Path -Parent $PSScriptRoot
$appProjectPath = Join-Path $repositoryRoot 'src\LiveAudioBoard.App\LiveAudioBoard.App.csproj'
$buildScriptPath = Join-Path $repositoryRoot 'scripts\build-release.ps1'
$releaseWorkflowPath = Join-Path $repositoryRoot '.github\workflows\release.yml'
$changelogPath = Join-Path $repositoryRoot 'CHANGELOG.md'

[xml] $appProject = Get-Content -LiteralPath $appProjectPath -Raw
$projectVersion = @($appProject.Project.PropertyGroup.Version |
    Where-Object { -not [string]::IsNullOrWhiteSpace($_) } |
    Select-Object -First 1)
if ($projectVersion.Count -ne 1) {
    throw 'LiveAudioBoard.App.csproj must contain exactly one non-empty Version value.'
}

$projectVersion = [string] $projectVersion[0]
if ([string]::IsNullOrWhiteSpace($Version)) {
    $Version = $projectVersion
}

if ($Version -ne $projectVersion) {
    throw "Requested version $Version does not match project version $projectVersion."
}

$buildScript = Get-Content -LiteralPath $buildScriptPath -Raw
if ($buildScript -notmatch "\[string\]\s+\`$Version\s*=\s*'([^']+)'") {
    throw 'Unable to find the default version in scripts/build-release.ps1.'
}
if ($Matches[1] -ne $Version) {
    throw "build-release.ps1 defaults to $($Matches[1]), expected $Version."
}

$releaseWorkflow = Get-Content -LiteralPath $releaseWorkflowPath -Raw
if ($releaseWorkflow -notmatch 'default:\s*"([^"]+)"') {
    throw 'Unable to find the workflow_dispatch version in release.yml.'
}
if ($Matches[1] -ne $Version) {
    throw "release.yml defaults to $($Matches[1]), expected $Version."
}

$changelog = Get-Content -LiteralPath $changelogPath -Raw
if ($changelog -notmatch [regex]::Escape("## [$Version]")) {
    throw "CHANGELOG.md does not contain a $Version release section."
}

$requiredFiles = @(
    'README.md',
    'README.en.md',
    'LICENSE',
    'COMMERCIAL_LICENSE.md',
    'THIRD_PARTY_NOTICES.md',
    'CHANGELOG.md',
    'SECURITY.md',
    'docs\USER_GUIDE.md',
    'docs\RELEASING.md'
)
foreach ($relativePath in $requiredFiles) {
    $path = Join-Path $repositoryRoot $relativePath
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) {
        throw "Required release document is missing: $relativePath"
    }
}

$license = Get-Content -LiteralPath (Join-Path $repositoryRoot 'LICENSE') -Raw
if ($license -notmatch 'PolyForm Noncommercial License 1\.0\.0' -or
    $license -notmatch 'Required Notice: Copyright \(c\) 2026 2683445453\.') {
    throw 'LICENSE is missing the expected PolyForm title or Required Notice.'
}

Write-Host "Release metadata verified for LiveAudioBoard $Version."
