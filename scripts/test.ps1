#!/usr/bin/env pwsh
Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = Resolve-Path (Join-Path $scriptDir '..')
Set-Location $repoRoot

function Resolve-SolutionPath {
    $preferred = Get-ChildItem -Path $repoRoot -Filter 'ArchIntel.sln' -File -Recurse | Sort-Object -Property FullName | Select-Object -First 1
    if ($null -ne $preferred) {
        return $preferred.FullName
    }

    $solutions = Get-ChildItem -Path $repoRoot -Filter '*.sln' -File -Recurse | Sort-Object -Property FullName
    if (-not $solutions) {
        throw "No solution file (*.sln) was found beneath '$repoRoot'."
    }

    if ($solutions.Count -gt 1) {
        Write-Warning "Multiple solution files found. Using '$($solutions[0].FullName)'."
    }

    return $solutions[0].FullName
}

$solutionPath = Resolve-SolutionPath
$resultsDirectory = Join-Path $repoRoot 'artifacts/test-results'
New-Item -ItemType Directory -Path $resultsDirectory -Force | Out-Null
Get-ChildItem -Path $resultsDirectory -Filter '*.trx' -File -ErrorAction SilentlyContinue | Remove-Item -Force

Write-Host "Using solution: $solutionPath"

dotnet restore $solutionPath
dotnet build $solutionPath -c Release --no-restore
dotnet test $solutionPath -c Release --no-build --logger 'trx' --results-directory $resultsDirectory
