#!/usr/bin/env bash
set -euo pipefail

dotnet restore ArchIntel.sln

dotnet build ArchIntel.sln -c Release --no-restore

dotnet test ArchIntel.sln -c Release --no-build --logger "trx;LogFileName=test-results.trx" --results-directory ./TestResults

dotnet format ArchIntel.sln --verify-no-changes
