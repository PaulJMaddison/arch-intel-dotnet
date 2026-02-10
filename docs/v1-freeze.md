# V1 freeze

This document freezes the ArchIntel v1.0 public surface.

## Purpose

The v1 freeze protects users from surprise changes while we complete the first commercial release. Until v1.1+, the CLI contract and artifact contract in this document are treated as stable.

## V1 feature surface

### CLI commands (supported in v1)

- `arch scan`
- `arch passport`
- `arch impact`
- `arch project-graph`
- `arch violations`

### CLI options and modes (supported in v1)

#### Required / command options

- `--solution <path>` for all commands.
- `--symbol <fully-qualified-name>` for `impact`.

#### Shared report options

- `--out <directory>`
- `--config <path>`
- `--format <json|md|both>` (default: `both`)
- `--fail-on-load-issues <true|false>`
- `--strict`
- `--include-doc-snippets`

#### Global options

- `--open`
- `--verbose`
- Built-in `--help` and `--version`

#### Runtime modes

- **Default mode:** non-fatal load issues are reported in artifacts; exit code remains success.
- **Strict mode (`--strict`):** CI-grade failure behavior for fatal load issues and rule violations.
- **Format mode:** JSON-only, Markdown-only, or both for report-specific artifacts.

### Artifact surface (supported in v1)

Every command run writes deterministic output files in the resolved output directory:

- `scan.json`
- `scan_summary.json`
- `projects.json`
- `packages.json`
- `symbols.json`
- `namespaces.json`
- `insights.json`
- `insights.md`
- `docs.json`
- `README.md`

Report-specific artifacts by command:

- `scan`: `scan.md` (when `--format md` or `both`)
- `passport`: `ARCHITECTURE_PASSPORT.md`
- `impact`: `impact.json` and/or `impact.md`
- `project-graph`: `project_graph.json` and/or `project_graph.md`
- `violations`: `violations.json` and/or `violations.md`

## Explicitly out of scope until v1.1+

The following are not v1 commitments and may only be introduced in v1.1 or later:

- New top-level commands.
- Renaming or removing existing commands, options, or artifact file names.
- Changing default behavior of `--format`, `--strict`, `--verbose`, or `--fail-on-load-issues`.
- Schema-breaking changes in JSON outputs (field renames, type changes, removals).
- Extension/plugin system, remote analysis, hosted service mode, and multi-repo orchestration.
- New commercial SLA tiers beyond the support policy defined for v1.

## Compatibility guarantees for v1

### CLI compatibility

For all v1 patch releases:

- Existing command names remain valid.
- Existing flags in this document remain valid.
- Existing flag semantics remain backward compatible.
- Existing exit-code semantics remain unchanged for default vs strict behavior.

### Artifact compatibility

For all v1 patch releases:

- Existing artifact file names in this document remain present.
- Existing JSON fields are append-only: new optional fields may be added, but existing fields are not removed or changed to incompatible types.
- Artifact ordering remains deterministic where currently deterministic (for stable diffs and CI review).

### Change control rule

Any proposal that violates these guarantees must be deferred to v1.1+ and called out explicitly in release notes.
