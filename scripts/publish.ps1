Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

$RepoRoot = Resolve-Path (Join-Path $PSScriptRoot "..")
$PropsPath = Join-Path $RepoRoot "Directory.Build.props"

$versionLine = Select-String -Path $PropsPath -Pattern "<Version>(.*)</Version>" | Select-Object -First 1
if (-not $versionLine) {
    throw "Unable to determine version from Directory.Build.props"
}

$version = $versionLine.Matches[0].Groups[1].Value
$releaseRoot = Join-Path $RepoRoot "releases"
$outputDir = Join-Path $releaseRoot "archintel-v$version-win-x64"
$zipPath = Join-Path $releaseRoot "archintel-v$version-win-x64.zip"

New-Item -ItemType Directory -Force -Path $releaseRoot | Out-Null

dotnet publish (Join-Path $RepoRoot "src/ArchIntel.Cli/ArchIntel.Cli.csproj") `
    -c Release `
    -r win-x64 `
    --self-contained false `
    -o $outputDir

if (Test-Path $zipPath) {
    Remove-Item $zipPath -Force
}

Compress-Archive -Path $outputDir -DestinationPath $zipPath
Write-Host "Release bundle created at: $zipPath"
