# Architecture

## Core concepts

### AnalysisContext
`AnalysisContext` is the immutable container for a single run. It captures the solution path, repo root, configuration, logger, and resolved output/cache directories so every stage shares the same runtime context.

### ProjectGraph
The project graph models project-to-project references and provides the backbone for dependency and layer analysis. It ensures cross-project relationships are consistent and deterministic for reporting.

### SymbolIndex
The symbol index is the global map of types, namespaces, and symbol locations. It enables fast lookups for impact analysis, reporting, and deterministic output ordering.

### Reports
Reports are emitted as structured JSON (and optional text) under `./.archtool/reports`. They are designed to be deterministic and diff-friendly for CI enforcement.

### Cache
The cache stores computed document hashes and scan results in `./.archtool/cache` to speed up repeated scans without sacrificing determinism.

### Determinism
Determinism is enforced by stable ordering, consistent hashing, and clear separation of input state. Running the same scan twice on the same inputs produces identical reports.
