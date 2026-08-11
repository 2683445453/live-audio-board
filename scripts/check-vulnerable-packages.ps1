[CmdletBinding()]
param(
    [string] $Solution = 'LiveAudioBoard.sln'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repositoryRoot = Split-Path -Parent $PSScriptRoot
if (-not [System.IO.Path]::IsPathRooted($Solution)) {
    $Solution = Join-Path $repositoryRoot $Solution
}

$jsonText = & dotnet list $Solution package --vulnerable --include-transitive --format json
if ($LASTEXITCODE -ne 0) {
    throw "dotnet package vulnerability scan failed with exit code $LASTEXITCODE."
}

$report = ($jsonText -join [Environment]::NewLine) | ConvertFrom-Json
$findings = @()
foreach ($project in @($report.projects)) {
    $frameworksProperty = $project.PSObject.Properties['frameworks']
    if ($null -eq $frameworksProperty) {
        continue
    }

    foreach ($framework in @($frameworksProperty.Value)) {
        foreach ($groupName in @('topLevelPackages', 'transitivePackages')) {
            $property = $framework.PSObject.Properties[$groupName]
            if ($null -eq $property) {
                continue
            }

            foreach ($package in @($property.Value)) {
                $vulnerabilities = $package.PSObject.Properties['vulnerabilities']
                if ($null -ne $vulnerabilities -and @($vulnerabilities.Value).Count -gt 0) {
                    $findings += [pscustomobject]@{
                        Project = $project.path
                        Framework = $framework.framework
                        Package = $package.id
                        Version = $package.resolvedVersion
                        Vulnerabilities = @($vulnerabilities.Value).Count
                    }
                }
            }
        }
    }
}

if ($findings.Count -gt 0) {
    $findings | Format-Table -AutoSize | Out-String | Write-Host
    throw "$($findings.Count) vulnerable package reference(s) found."
}

Write-Host "NuGet vulnerability scan passed for $(@($report.projects).Count) project(s)."
