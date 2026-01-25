# Release checklist

## Pre-release
- All CI workflows are green.
- Run `dotnet build -c Release` and `dotnet test -c Release` locally.
- Bump version numbers where applicable.
- Update `CHANGELOG.md` (if present).
- Review docs and update screenshots or examples if needed.

## Release build
- Perform a clean build (`dotnet clean`, then `dotnet build -c Release`).
- Run `dotnet pack -c Release` to generate NuGet artifacts.
- Verify nupkg metadata (version, description, repository URL).

## Validation
- Run the CLI against a known sample repository.
- Perform the determinism check by running the same scan twice and diffing outputs.

## Publish (future-facing)
- Create and push a Git tag for the release.
- Draft GitHub release notes.
- Publish NuGet packages (placeholder until enabled).

## Post-release
- Announce the release in the appropriate channels.
- Monitor feedback and open issues for regressions.
