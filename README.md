# ArchIntel

**Local first, architecture aware analysis for large .NET solutions.** ArchIntel runs entirely on your machine against cloned repositories. It is designed for enterprise teams that need deterministic, scalable architecture insights without uploading source code anywhere.

## What this is
- A .NET 8 CLI (`arch`) that inspects solution structure and produces machine-readable reports.
- Built for large, multi-project solutions with deterministic outputs suitable for CI diffing.
- Designed for local-first operation: no network calls, no databases, no background services.

## Who it’s for
- Architects and platform teams enforcing guardrails across large solutions.
- Engineering leads who need scalable, repeatable insights in CI.
- Security and privacy conscious teams that cannot upload source code.

## What makes it different
- **Local-first by default.** Runs on cloned repos with zero external dependencies.
- **Deterministic outputs.** Stable report formats that can be diffed in CI.
- **Scalable execution.** Bounded parallelism and incremental friendly caching directories.
- **No source exposure.** Logging never dumps source code.

## What it is not
- A hosted SaaS or cloud only analyzer.
- A background daemon or always on service.
- A replacement for full static analysis suites.

## Zero-infra / local-first design
ArchIntel is intentionally simple: no network calls, no database, and no background services. All state is stored in `./.archtool/` within your repo clone for easy cleanup and deterministic output.

## Quickstart
```bash
# Scan a solution
arch scan --solution ./MySolution.sln

# Generate an architecture passport
arch passport --solution ./MySolution.sln --format both

# Impact analysis for a symbol
arch impact --solution ./MySolution.sln --symbol My.Namespace.Type --format json
```

### Config file
ArchIntel reads configuration from `./.archtool/config.json` by default, or from `--config`.

```json
{
  "includeGlobs": ["**/*.cs"],
  "excludeGlobs": ["**/bin/**", "**/obj/**"],
  "outputDir": "./.archtool/reports",
  "cacheDir": "./.archtool/cache",
  "maxDegreeOfParallelism": 8
}
```

## Works without AI. Optional AI guidance.
ArchIntel works entirely without AI. If you choose to integrate AI guidance, that is optional and must be configured outside of the core tool. The open-source CLI never uploads source code by default.

## Pro & Enterprise
We offer commercial licensing for teams that want specialized outcomes: custom architecture policies, private extensions, and enterprise support. This is outcome based only; no roadmap promises are made in the open source repo.

## License
This project is dual-licensed under AGPLv3 and a commercial license. See [LICENSE](LICENSE) and [COMMERCIAL-LICENSE.md](COMMERCIAL-LICENSE.md).
