# Release checklist

## Pre-release
- Ensure CI is green on `main`.
- Run locally:
  - `dotnet build -c Release`
  - `dotnet test -c Release`
- Confirm documentation and README examples are up to date.
- Confirm version numbers are set for the target release (e.g., `0.1.0`, `0.2.0`).

## Release build (win-x64)
- Run the publish script:
  - PowerShell: `./scripts/publish.ps1`
  - Bash: `./scripts/publish.sh`
- Verify the output zip in `./releases/archintel-v<version>-win-x64.zip`.
- Smoke test by running `arch --version` from the published output.

## Tag + publish
- Create a signed tag: `git tag -a v0.1.0 -m "v0.1.0"` (adjust version).
- Push tags: `git push origin v0.1.0`.
- Draft GitHub release notes and upload the zip from `releases/`.

## Post-release
- Announce the release in the appropriate channels.
- Open follow-up issues for any regressions or post-release fixes.
