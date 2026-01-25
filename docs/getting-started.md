# Getting started (5-minute guide)

## 1) Install
Grab the release bundle (for Windows, `releases/archintel-v0.1.0-win-x64.zip`) and unzip it. Add the folder to your PATH or run `arch` from the extracted directory.

## 2) Run a scan
From your repo root:

```bash
arch scan --solution ./YourSolution.sln
```

By default, outputs go to `./.archintel` next to the solution.

## 3) Inspect outputs
The output folder contains:
- `scan.json` (config receipt)
- `scan_summary.json` (load + scan summary)
- `symbols.json` and `namespaces.json` (symbol index)
- report-specific files (for example `project_graph.json`)

Open `./.archintel/README.md` for a full explanation of each file and suggested AI copy/paste usage.

## 4) Optional flags
- `--format both` (default) writes JSON and Markdown for report-specific outputs.
- `--strict` enables CI-grade exit codes.
- `--verbose` prints full MSBuildWorkspace diagnostics.

## Recommended branch protection
For GitHub repositories, we recommend:
- Require pull requests before merging.
- Require CI checks to pass.
- Block force pushes on `main`.
