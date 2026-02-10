# Support policy (v1)

This policy defines how ArchIntel v1 issues are supported, triaged, and resolved.

## Scope of support

### Supported in v1

- Official ArchIntel CLI commands and flags documented in `docs/v1-freeze.md`.
- Artifacts emitted by those commands (`*.json`, `*.md`, and output `README.md`).
- Running against standard .NET solutions that can be loaded by local MSBuild tooling.

### Not supported in v1

- Custom forks with modified CLI contracts.
- Unpublished/internal commands or undocumented flags.
- Feature requests that require new command surfaces during v1 freeze.
- Environment failures unrelated to ArchIntel logic (for example missing SDK/workload), except for best-effort troubleshooting guidance.

## Issue intake

When filing a bug, include:

- ArchIntel version (`arch --version`).
- Full command executed.
- Relevant output artifacts (at minimum `scan_summary.json` and `scan.json`).
- Reproduction steps and expected vs actual behavior.

Issues without minimum reproduction data may be labeled "need-info" until details are provided.

## Triage model

All incoming issues are classified by type and severity.

### Type

- **Bug:** behavior contradicts documented v1 behavior.
- **Regression:** behavior worked in a prior release and fails now.
- **Documentation:** docs are inaccurate or incomplete.
- **Feature request:** enhancement outside the v1 frozen scope.

### Severity

- **S1 (critical):** data corruption, unusable CLI, or blocking crash with no workaround.
- **S2 (high):** major feature broken with limited workaround.
- **S3 (medium):** partial degradation; acceptable workaround exists.
- **S4 (low):** minor defect, UX polish, or non-blocking docs issue.

## Response expectations

- S1/S2 bugs are prioritized for the next patch release.
- S3/S4 bugs are prioritized by impact and grouped into planned maintenance.
- Feature requests are reviewed for v1.1+ planning and are not treated as v1 blockers unless they represent a hidden regression.

## Fix policy during v1 freeze

- Only backward-compatible fixes are accepted in v1 patch releases.
- Any change that would break CLI/artefact compatibility is deferred to v1.1+.
- Security or correctness fixes may add new optional fields to JSON outputs but must not remove or rename existing ones.

## End of v1 policy

This policy applies to the v1 major line. A revised policy will be published with v1.1+ if support tiers, lifecycle windows, or compatibility guarantees change.
