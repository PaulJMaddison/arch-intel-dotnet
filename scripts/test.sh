#!/usr/bin/env bash
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/.." && pwd)"
cd "$REPO_ROOT"

resolve_solution_path() {
  local preferred
  preferred="$(find "$REPO_ROOT" -type f -name 'ArchIntel.sln' | sort | head -n 1 || true)"
  if [[ -n "$preferred" ]]; then
    printf '%s\n' "$preferred"
    return
  fi

  mapfile -t solutions < <(find "$REPO_ROOT" -type f -name '*.sln' | sort)
  if [[ ${#solutions[@]} -eq 0 ]]; then
    echo "No solution file (*.sln) was found beneath '$REPO_ROOT'." >&2
    return 1
  fi

  if [[ ${#solutions[@]} -gt 1 ]]; then
    echo "Warning: multiple solution files found. Using '${solutions[0]}'." >&2
  fi

  printf '%s\n' "${solutions[0]}"
}

SOLUTION_PATH="$(resolve_solution_path)"
RESULTS_DIRECTORY="$REPO_ROOT/artifacts/test-results"
mkdir -p "$RESULTS_DIRECTORY"
rm -f "$RESULTS_DIRECTORY"/*.trx

echo "Using solution: $SOLUTION_PATH"

dotnet restore "$SOLUTION_PATH"
dotnet build "$SOLUTION_PATH" -c Release --no-restore
dotnet test "$SOLUTION_PATH" -c Release --no-build --logger "trx" --results-directory "$RESULTS_DIRECTORY"
