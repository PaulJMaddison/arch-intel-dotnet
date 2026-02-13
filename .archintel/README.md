# ArchIntel output notes

Method surface metrics in `scan_summary.json` and `namespaces.json`:

- `DeclaredPublicMethodCount`: methods declared `public`, regardless of containing type visibility.
- `PubliclyReachableMethodCount`: methods declared `public` where the containing type (and all containing types in the chain) are also `public`.
- `DeprecatedPublicMethodCount` and `PublicMethodCount`: compatibility aliases for `DeclaredPublicMethodCount`.
