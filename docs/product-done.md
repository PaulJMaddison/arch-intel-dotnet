# Product finishing gate audit (Task 0)

This document audits the current repository state against the commercial product outcomes explicitly called out in the repo messaging: architecture governance, performance/scalability analysis, private extensions, and enterprise support.

## 1) What exists now

### 1.1 Open-source product baseline (implemented)
- A local-first .NET 8 CLI (`arch`) with deterministic reports and no required external services.
- Commands available in the CLI: `scan`, `passport`, `impact`, `project-graph`, and `violations`.
- Deterministic report pipeline with JSON/Markdown output options.
- Strict-mode CI-friendly exit codes.
- Architecture rules + drift detection capability.
- Caching and symbol index support.
- Packaging and release scripts for publish bundle generation.
- CI/security workflows present (`ci`, `codeql`, `dependency-review`, `oss-guardrails`).

### 1.2 Evidence map (implemented artifacts)
- Product claims and positioning:
  - `README.md`
  - `COMMERCIAL-LICENSE.md`
- CLI surface and execution:
  - `src/ArchIntel.Cli/Program.cs`
  - `src/ArchIntel.Cli/CliExecutor.cs`
  - `src/ArchIntel.Cli/ExitCodes.cs`
- Core analysis/reports:
  - `src/ArchIntel.Core/Analysis/*`
  - `src/ArchIntel.Core/Reports/*`
  - `src/ArchIntel.Core/Configuration/*`
- Packaging/build/release:
  - `Directory.Build.props`
  - `scripts/publish.sh`
  - `scripts/publish.ps1`
  - `docs/release-checklist.md`
- Quality/automation:
  - `.github/workflows/ci.yml`
  - `.github/workflows/codeql.yml`
  - `.github/workflows/dependency-review.yml`
  - `.github/workflows/oss-guardrails.yml`
  - `tests/ArchIntel.Tests/*`

## 2) What is missing (with file paths)

The repository explicitly states commercial outcomes, but implementation artifacts for those commercial modules are not present in-tree.

### 2.1 Missing outcome: performance and scalability analysis module
- Outcome reference exists in: `README.md` (commercial modules list).
- Missing implementation paths:
  - `src/ArchIntel.Pro.Performance/` (directory not present)
  - `src/ArchIntel.Core/Reports/PerformanceAnalysisReport.cs` (file not present)
  - `src/ArchIntel.Cli` command wiring for a performance command (no `performance` command in `Program.cs`)

### 2.2 Missing outcome: private extensions model
- Outcome reference exists in: `README.md` (private extensions).
- Missing implementation paths:
  - `src/ArchIntel.Core/Extensions/` (directory not present)
  - `src/ArchIntel.Core/Extensions/IExtensionModule.cs` (file not present)
  - `docs/extensions.md` (file not present)

### 2.3 Missing outcome: enterprise support delivery artifacts
- Outcome reference exists in: `README.md` (enterprise support).
- Missing implementation/documentation paths:
  - `docs/support/sla.md` (file not present)
  - `docs/support/escalation-policy.md` (file not present)
  - `docs/support/operational-handbook.md` (file not present)

### 2.4 Missing commercialization packaging boundary
- Dual-license text is present, but no explicit in-repo module boundary for commercial-only code.
- Missing structural paths:
  - `src/ArchIntel.Pro/` (directory not present)
  - `src/ArchIntel.Enterprise/` (directory not present)
  - `docs/commercial/module-boundaries.md` (file not present)

## 3) Ordered execution plan for remaining tasks

This plan is based on current repository contents and minimizes risk by preserving the stable OSS CLI while adding additive commercial capability seams.

1. **Define commercial module boundaries and contracts first**
   - Create architecture + policy docs before coding modules.
   - Target files:
     - `docs/commercial/module-boundaries.md` (new)
     - `docs/commercial/packaging-strategy.md` (new)
     - `docs/commercial/versioning-compatibility.md` (new)

2. **Add extension/plugin seam in core**
   - Introduce an extension contract that allows optional module loading while preserving deterministic defaults.
   - Target paths:
     - `src/ArchIntel.Core/Extensions/` (new)
     - `src/ArchIntel.Core/Analysis/` (wire optional extension hook)
     - `tests/ArchIntel.Tests/Extensions/` (new test coverage)

3. **Implement performance/scalability analysis as first commercial module**
   - Add report model + engine + CLI command with deterministic output ordering.
   - Target paths:
     - `src/ArchIntel.Pro.Performance/` (new)
     - `src/ArchIntel.Core/Reports/PerformanceAnalysisReport.cs` (new)
     - `src/ArchIntel.Cli/Program.cs` (add command)
     - `src/ArchIntel.Cli/CliExecutor.cs` (execution + exit behavior)
     - `tests/ArchIntel.Tests/Performance*` (new)

4. **Formalize enterprise support artifacts**
   - Add support readiness docs required for commercial delivery operations.
   - Target paths:
     - `docs/support/sla.md` (new)
     - `docs/support/escalation-policy.md` (new)
     - `docs/support/operational-handbook.md` (new)

5. **Add commercialization packaging and release automation**
   - Split OSS vs commercial packaging outputs and release checklists.
   - Target paths:
     - `scripts/publish.sh` and `scripts/publish.ps1` (update)
     - `docs/release-checklist.md` (update with commercial tracks)
     - `.github/workflows/` (add/revise release workflows)

6. **Hardening and acceptance gate**
   - Ensure CI, deterministic output checks, and licensing/legal checks pass for both OSS and commercial tracks.
   - Target paths:
     - `.github/workflows/ci.yml` (update matrix if needed)
     - `.github/workflows/oss-guardrails.yml` (update)
     - `tests/ArchIntel.Tests/` (add regression coverage)
     - `docs/product-done.md` (refresh at gate close)

## 4) Done criteria for this Task 0 gate
- A single deterministic audit document exists at `docs/product-done.md`.
- No source-code behavior changes were made.
