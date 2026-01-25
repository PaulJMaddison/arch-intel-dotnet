#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
version="$(sed -n 's/.*<Version>\(.*\)<\/Version>.*/\1/p' "$repo_root/Directory.Build.props" | head -n 1)"

if [[ -z "$version" ]]; then
  echo "Unable to determine version from Directory.Build.props" >&2
  exit 1
fi

release_root="$repo_root/releases"
output_dir="$release_root/archintel-v${version}-win-x64"
zip_path="$release_root/archintel-v${version}-win-x64.zip"

mkdir -p "$release_root"

dotnet publish "$repo_root/src/ArchIntel.Cli/ArchIntel.Cli.csproj" \
  -c Release \
  -r win-x64 \
  --self-contained false \
  -o "$output_dir"

(
  cd "$release_root"
  rm -f "$zip_path"
  zip -r "$(basename "$zip_path")" "$(basename "$output_dir")"
)

echo "Release bundle created at: $zip_path"
