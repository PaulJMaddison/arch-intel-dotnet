# Definition of done (v1)

This checklist defines "ready to ship v1" for ArchIntel.

## Product contract

- [ ] v1 feature surface is frozen and documented (`docs/v1-freeze.md`).
- [ ] No breaking changes to CLI commands, flags, or exit-code semantics.
- [ ] No breaking changes to artifact file names or JSON schema.
- [ ] All user-facing behavior changes are documented in release notes.

## Engineering quality

- [ ] `dotnet build` succeeds for the CLI in Release configuration.
- [ ] `dotnet test` succeeds for the test suite in Release configuration.
- [ ] Smoke run from source succeeds against the sample solution.
- [ ] Output artifacts are deterministic across repeated runs on unchanged input.
- [ ] No TODO-only stubs or placeholder behavior in shipped paths.

## Documentation quality

- [ ] Getting-started docs match actual CLI behavior and flags.
- [ ] Troubleshooting guidance reflects current diagnostics and strict/default behavior.
- [ ] Support policy is published and consistent with operational expectations.
- [ ] Release checklist is current for v1 release mechanics.

## Release readiness

- [ ] Version number and tag target for v1 are set.
- [ ] Release bundle is produced and smoke-tested.
- [ ] Rollback plan for v1 release is documented.
- [ ] Post-release ownership for triage/support is assigned.

## Sign-off

- [ ] Engineering sign-off
- [ ] Product sign-off
- [ ] Support/operations sign-off
