# ArchIntel

![CI Disabled](https://img.shields.io/badge/CI-disabled-success)
[![License: AGPL-3.0](https://img.shields.io/badge/License-AGPL%203.0-blue.svg)](LICENSE)

**Local folder architecture intelligence for large .NET solutions.** ArchIntel is a .NET 8 CLI (`arch`) that runs entirely on your machine, producing deterministic reports without sending source code anywhere.

## Elevator pitch
ArchIntel gives you the architecture visibility of an enterprise governance platform in a single local CLI. Point it at a solution, run one command, and get actionable architecture reports your team can use immediately in planning, modernization, onboarding, and CI guardrails.

If your codebase is too large for gut-feel decisions, ArchIntel helps you replace assumptions with evidence.

## Why teams choose ArchIntel
- **Ship change with confidence.** Understand dependency blast radius before touching core services.
- **Reduce architecture drift.** Catch layer violations and risky coupling trends early.
- **Move faster in big repos.** Give new contributors a map instead of asking them to reverse-engineer the system.
- **Support modernization with facts.** Prioritize refactors using repeatable reports, not anecdotes.
- **Keep compliance simple.** Analyze locally and keep sensitive source code in your environment.

## Outcomes you can expect
- Faster technical discovery during sprint planning and incident response.
- Clearer architecture conversations between architects, staff engineers, and feature teams.
- More predictable CI quality gates with deterministic, diff-friendly outputs.
- Less time wasted chasing hidden dependencies across solution boundaries.

## Why ArchIntel
- **Local folder** Runs against cloned repositories with zero external dependencies.
- **Architecture intelligence.** Produces structured insights about projects, layers, dependencies, and drift.
- **Zero infra.** No servers, no databases, no background services; just run the CLI.
- **Deterministic outputs.** Stable report formats suitable for CI diffing and enforcement.

## Who it’s for
- Architects and platform teams enforcing guardrails across large solutions.
- Engineering leads who need repeatable insights in CI and local workflows.
- Security and privacy conscious teams that keep source code on device.

### Ideal use cases
- **Monolith-to-modular initiatives:** map boundaries and identify high-coupling hotspots.
- **M&A / inherited codebases:** rapidly understand unknown solutions before major changes.
- **Regulated environments:** keep analysis local while still producing governance artifacts.
- **Platform engineering:** standardize architecture checks across many repositories.
- **Developer onboarding:** shorten time-to-first-meaningful-change with architecture passports.

## Why ArchIntel vs. generic tooling
- Built specifically for **large .NET solution structures** and architecture-level understanding.
- Produces **deterministic machine-readable artifacts** you can plug into CI, scripts, and custom workflows.
- Designed for **local-first operation** with zero always-on infrastructure.
- Gives teams a pragmatic middle ground between ad-hoc scripts and heavyweight enterprise platforms.

## What it is not
- A hosted SaaS or cloud only analyzer.
- A background daemon or always on service.
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

By default, reports are written to `./.archintel` alongside the solution you are analysing.

### Run from source
Use the CLI project directly when validating local changes:

```bash
# Build the CLI project
dotnet build ./src/ArchIntel.Cli/ArchIntel.Cli.csproj -c Release

# Show version/help
dotnet run --project ./src/ArchIntel.Cli/ArchIntel.Cli.csproj -- --version
dotnet run --project ./src/ArchIntel.Cli/ArchIntel.Cli.csproj -- --help

# Run a report command against a solution
dotnet run --project ./src/ArchIntel.Cli/ArchIntel.Cli.csproj -- scan --solution ./MySolution.sln --format both
```

## Docs
- [Getting started](docs/getting-started.md)
- [Troubleshooting](docs/troubleshooting.md)
- [Release checklist](docs/release-checklist.md)
- [Architecture](docs/architecture.md)

## Developer Workflow
Run the build scripts from the repo root to restore, build, test, and verify formatting:
- macOS/Linux: `./build.sh`
- Windows (PowerShell): `./build.ps1`
- Windows smoke matrix: `./scripts/smoke.ps1 -Solution ./YourSolution.sln -OutDir ./artifacts/smoke`

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
Use `--strict` in CI to turn load issues and architecture violations into non zero exit codes while still writing reports.

```bash
# Strict mode for CI (fails on load issues or violations)
arch scan --solution ./MySolution.sln --strict

# Strict mode violations report
arch violations --solution ./MySolution.sln --strict
```

Exit codes:
- `0`: analysis completed (default mode may still include non fatal load issues).
- `1`: fatal load failure (solution could not be opened, 0 projects loaded, SDK missing).
- `2`: strict mode gates tripped (load issues and/or architecture violations).
- `3`: unexpected crash (unhandled exception).

## Release bundle (win-x64)
Download the zip from `releases/archintel-v1.0.0-win-x64.zip`, unzip it, and run `arch` from a terminal.

## Performance
- **Caching:** the cache under `./.archintel/cache` stores document hashes to speed up repeat scans without changing outputs.
- **Large repos:** expect solution load time to dominate for very large solutions; scans scale with project count and file size.
- **Knobs:** `maxDegreeOfParallelism` controls parallel symbol indexing and scan throughput. Keep it under your CPU core count for predictable performance.

## Works without AI. Optional AI guidance.
ArchIntel works entirely without AI. If you choose to integrate AI guidance, that is optional and must be configured outside of the core tool. The open-source CLI never uploads source code by default. 

## Using ArchIntel outputs with your own AI (no repo upload)

Run ArchIntel locally:

```bash
arch scan --solution <path>
```

Then paste these artifacts into your AI assistant:

- `.archintel/scan_summary.json`
- `.archintel/projects.json`
- `.archintel/project_graph.json`
- `.archintel/namespaces.json`
- `.archintel/symbols.json`
- `.archintel/packages.json`
- `.archintel/docs.json`
- `.archintel/violations.json`

This gives you architecture summaries and remediation plans **without zipping or uploading source code**. It works well for huge and private repos because only deterministic artifacts are shared.

## Library / NuGet note: 
v1.0.0 focuses on the CLI and report artifacts. NuGet packages may be introduced in a future release to support integrations (e.g., IDE or agent layers). Until then, public compatibility guarantees apply only to the CLI contract and output formats.

## Pro & Enterprise
Commercial features are delivered outside this OSS repository. The OSS CLI remains local-first and does not include Copilot/AI modules.

- Persona reports (architect / new dev / contractor onboarding)
- Ask/Q&A over ArchIntel artifacts with guardrails (no source code ingestion)
- Context pack generation v2 with derived metrics (fan-in/out, core/risky projects, cycle severity)
- Performance module (code + IO + ORM + DB guidance; trace capture workflow)
- User story module (targets relevant files using ArchIntel index to reduce AI cost; optional safe patch plan)
- PR-ready change plan module (Mode C: patch plan, safety gates, dry-run)
- Enterprise support + private extensions

Contact: paul.maddison.delimeg@gmail.com

Commercial capabilities are outcome-based. No public roadmap commitments are made in the open-source repository.

## License
This project is dual-licensed under AGPLv3 and a commercial license. See [LICENSE](LICENSE) and [COMMERCIAL-LICENSE.md](COMMERCIAL-LICENSE.md).
