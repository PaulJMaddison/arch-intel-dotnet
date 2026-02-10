Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

param(
    [Parameter(Mandatory = $true)]
    [string]$Solution,

    [Parameter(Mandatory = $true)]
    [string]$OutDir,

    [string]$Configuration = "Release"
)

$RepoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$CliProject = Join-Path $RepoRoot "src/ArchIntel.Cli/ArchIntel.Cli.csproj"
$ResolvedSolution = Resolve-Path $Solution

if (-not (Test-Path $CliProject)) {
    throw "CLI project was not found at '$CliProject'."
}

New-Item -ItemType Directory -Force -Path $OutDir | Out-Null
$ResolvedOutDir = Resolve-Path $OutDir

Write-Host "Building ArchIntel CLI..."
dotnet build $CliProject -c $Configuration

Write-Host "Running --version check..."
$versionOutput = dotnet run --project $CliProject -c $Configuration -- --version 2>&1
if ($LASTEXITCODE -ne 0) {
    throw "--version failed with exit code $LASTEXITCODE. Output: $versionOutput"
}
if ($versionOutput -notmatch '[0-9]+\.[0-9]+\.[0-9]+') {
    throw "--version did not contain a semantic version. Output: $versionOutput"
}

Write-Host "Running --help check..."
$helpOutput = dotnet run --project $CliProject -c $Configuration -- --help 2>&1
if ($LASTEXITCODE -ne 0) {
    throw "--help failed with exit code $LASTEXITCODE. Output: $helpOutput"
}
$expectedCommands = @("scan", "passport", "project-graph", "violations")
foreach ($command in $expectedCommands) {
    if ($helpOutput -notmatch "\\b$([Regex]::Escape($command))\\b") {
        throw "--help output did not include '$command'."
    }
}

$commandMatrix = @(
    @{ Name = "scan"; Args = @("scan", "--solution", $ResolvedSolution, "--format", "both") },
    @{ Name = "passport"; Args = @("passport", "--solution", $ResolvedSolution, "--format", "both") },
    @{ Name = "project-graph"; Args = @("project-graph", "--solution", $ResolvedSolution, "--format", "both") },
    @{ Name = "violations"; Args = @("violations", "--solution", $ResolvedSolution, "--format", "both") }
)

foreach ($entry in $commandMatrix) {
    $name = $entry.Name
    $commandOutDir = Join-Path $ResolvedOutDir $name
    New-Item -ItemType Directory -Force -Path $commandOutDir | Out-Null

    $args = @("run", "--project", $CliProject, "-c", $Configuration, "--") + $entry.Args + @("--out", $commandOutDir)
    Write-Host "Running $name smoke command..."
    dotnet @args

    if ($LASTEXITCODE -ne 0) {
        throw "Smoke command '$name' failed with exit code $LASTEXITCODE."
    }
}

Write-Host "Smoke matrix completed successfully. Outputs written to '$ResolvedOutDir'."
