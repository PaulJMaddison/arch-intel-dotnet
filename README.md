# ArchIntel

[![CI](https://github.com/arch-intel/arch-intel-dotnet/actions/workflows/ci.yml/badge.svg)](https://github.com/arch-intel/arch-intel-dotnet/actions/workflows/ci.yml)
[![License: AGPL-3.0](https://img.shields.io/badge/License-AGPL%203.0-blue.svg)](LICENSE)

**Local-first architecture intelligence for large .NET solutions.** ArchIntel is a .NET 8 CLI (`arch`) that runs entirely on your machine, producing deterministic reports without sending source code anywhere.

## Why ArchIntel
- **Local-first.** Runs against cloned repositories with zero external dependencies.
- **Architecture intelligence.** Produces structured insights about projects, layers, dependencies, and drift.
- **Zero infra.** No servers, no databases, no background services; just run the CLI.
- **Deterministic outputs.** Stable report formats suitable for CI diffing and enforcement.

## Who it’s for
- Architects and platform teams enforcing guardrails across large solutions.
- Engineering leads who need repeatable insights in CI and local workflows.
- Security and privacy-conscious teams that keep source code on-device.

## What it is not
- A hosted SaaS or cloud-only analyzer.
- A background daemon or always-on service.
- A replacement for full static analysis suites.

## Zero-infra / local-first design
ArchIntel is intentionally simple: no network calls, no databases, and no background services. All state is stored in `./.archintel/` within your repo clone for easy cleanup and deterministic output.

## Quickstart
```bash
# Scan a solution
arch scan --solution ./MySolution.sln

# Generate an architecture passport
arch passport --solution ./MySolution.sln --format both

# Impact analysis for a symbol
arch impact --solution ./MySolution.sln --symbol My.Namespace.Type --format json

# Open the output directory after completion
arch scan --solution ./MySolution.sln --open
```

By default, reports are written to `./.archintel` alongside the solution.

## Docs
- [Getting started](docs/getting-started.md)
- [Troubleshooting](docs/troubleshooting.md)
- [Release checklist](docs/release-checklist.md)
- [Architecture](docs/architecture.md)

## Developer Workflow
Run the build scripts from the repo root to restore, build, test, and verify formatting:
- macOS/Linux: `./build.sh`
- Windows (PowerShell): `./build.ps1`

### Config file
ArchIntel reads configuration from `./.archintel/config.json` by default, or from `--config`.

```json
{
  "includeGlobs": ["**/*.cs"],
  "excludeGlobs": ["**/bin/**", "**/obj/**"],
  "outputDir": "./.archintel",
  "cacheDir": "./.archintel/cache",
  "maxDegreeOfParallelism": 8,
  "strict": {
    "failOnLoadIssues": true,
    "failOnViolations": true
  }
}
```

## CI / strict mode
Use `--strict` in CI to turn load issues and architecture violations into non-zero exit codes while still writing reports.

```bash
# Strict mode for CI (fails on load issues or violations)
arch scan --solution ./MySolution.sln --strict

# Strict mode violations report
arch violations --solution ./MySolution.sln --strict
```

Exit codes:
- `0`: analysis completed (default mode may still include non-fatal load issues).
- `1`: fatal load failure (solution could not be opened, 0 projects loaded, SDK missing).
- `2`: strict mode gates tripped (load issues and/or architecture violations).
- `3`: unexpected crash (unhandled exception).

## Release bundle (win-x64)
Download the zip from `releases/archintel-v0.1.0-win-x64.zip`, unzip it, and run `arch` from a terminal.

## Performance
- **Caching:** the cache under `./.archintel/cache` stores document hashes to speed up repeat scans without changing outputs.
- **Large repos:** expect solution load time to dominate for very large solutions; scans scale with project count and file size.
- **Knobs:** `maxDegreeOfParallelism` controls parallel symbol indexing and scan throughput. Keep it under your CPU core count for predictable performance.

## Works without AI. Optional AI guidance.
ArchIntel works entirely without AI. If you choose to integrate AI guidance, that is optional and must be configured outside of the core tool. The open-source CLI never uploads source code by default.

## Pro & Enterprise
We offer commercial licensing for teams that want specialized outcomes: custom architecture policies, private extensions, and enterprise support. This is outcome based only; no roadmap promises are made in the open source repo.

## License
This project is dual-licensed under AGPLv3 and a commercial license. See [LICENSE](LICENSE) and [COMMERCIAL-LICENSE.md](COMMERCIAL-LICENSE.md).
