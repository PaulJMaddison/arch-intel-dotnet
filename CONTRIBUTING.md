# Contributing to ArchIntel

Thank you for your interest in contributing. We welcome pull requests that improve stability, documentation, and analysis quality.

## Ground rules
- Be respectful and professional.
- Keep changes focused and well-tested.
- Ensure outputs are deterministic and suitable for CI diffing.
- Do not add features that are exclusive to Pro/Enterprise offerings in the open-source edition.

## Development workflow
1. Create a feature branch.
2. Make your changes with clear commit messages.
3. Run `dotnet build` and `dotnet test`.
4. Open a pull request with a clear description.

## Code quality
- Use nullable reference types and treat warnings as errors.
- Prefer bounded parallelism.
- Avoid logging source code or sensitive content.
- Keep new dependencies minimal and well-justified.

## CLA
By contributing, you agree that your contributions will be licensed under AGPLv3 unless a separate agreement is made.
