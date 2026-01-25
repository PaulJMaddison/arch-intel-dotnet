# Troubleshooting

## MSBuildWorkspace quirks
ArchIntel relies on `MSBuildWorkspace` to load solutions. It uses your local SDK and tooling, so the most common issues are related to SDK resolution, missing workloads, or mismatched solution expectations.

Tips:
- Run `dotnet build` from the solution directory to confirm the SDK can load the solution.
- Use `arch --verbose ...` to see full MSBuildWorkspace diagnostics (non-verbose mode only prints a summary).
- Ensure any required workloads (e.g., desktop or MAUI) are installed for the solution.

## SDK pinning via `global.json`
If your solution requires a specific SDK, pin it with a `global.json`:

```json
{
  "sdk": {
    "version": "8.0.204"
  }
}
```

Then run `dotnet --list-sdks` to confirm the pinned SDK is installed.

## Strict vs default mode
ArchIntel always writes reports, even if MSBuild returns non-fatal load issues. The difference is how exit codes are handled:
- **Default mode:** exit code is `0` for non-fatal load issues; consult `scan_summary.json`.
- **Strict mode (`--strict`):** exit code is `2` if load issues or architecture violations are detected.

You can also set strict defaults in `.archintel/config.json` via the `strict` block.

## scan_summary.json fields
`scan_summary.json` includes a `Counts` block with the following fields:
- **ProjectCount:** number of projects in the loaded solution (`Solution.Projects.Count`).
- **FailedProjectCount:** best-effort count of projects that failed to load. If a fatal diagnostic cannot be mapped to a specific project, it is still counted as a failure.
- **AnalyzedDocuments:** number of documents actually processed by the scanner (after exclusions).

`ProjectCount` can be `0` when MSBuild is unable to load any projects (for example, missing SDKs or workloads). In that case, ArchIntel exits with a non-zero code even in default mode.

## Common solution load issues
- **"MSBuild SDKs were not found"**: install the .NET SDK or Visual Studio Build Tools.
- **"The SDK 'Microsoft.NET.Sdk' specified could not be found"**: verify your `global.json` and SDK installation.
- **"Unable to load project file"**: check workloads, restore packages, and make sure the project builds locally.
- **"NUxxxx" warnings**: restore packages and review version conflicts.

If issues persist, try `arch --verbose scan --solution <path>` and review the diagnostics in `scan_summary.json`.
