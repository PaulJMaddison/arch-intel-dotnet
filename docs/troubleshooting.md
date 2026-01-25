# Troubleshooting

## MSBuildWorkspace setup
ArchIntel relies on `MSBuildWorkspace` to load solutions. Ensure the .NET SDK is installed and that the target solution builds locally with `dotnet build`.

If you see MSBuild-related errors:
- Verify the correct .NET SDK version is installed (8.0+).
- Ensure environment variables like `MSBuildSDKsPath` are not misconfigured.
- Try running `dotnet build` from the solution directory to validate SDK resolution.

## Common SDK errors
- **"MSBuild SDKs were not found"**: Install the .NET SDK or Visual Studio Build Tools.
- **"Failed to load the solution with MSBuild"**: Restore packages and ensure the solution builds locally.

## Performance tips for large solutions
- Increase `maxDegreeOfParallelism` in `.archtool/config.json` to match CPU capacity.
- Use `excludeGlobs` to skip `bin/`, `obj/`, generated code, or large vendor folders.
- Keep caches in `.archtool/cache` to speed up repeat scans.

## CI exit codes
ArchIntel reports CI-friendly exit codes when running with `--strict`:
- `0`: analysis completed (default mode may still include non-fatal load issues).
- `1`: fatal load failure (solution could not be opened, 0 projects loaded, SDK missing).
- `2`: strict mode gates tripped (load issues and/or architecture violations).
- `3`: unexpected crash (unhandled exception).

If you see exit code `2`, inspect the reports directory (for example, `scan_summary.json` or `violations.json`) for details. When running without `--strict`, non-fatal load diagnostics are summarized but do not change the exit code.
