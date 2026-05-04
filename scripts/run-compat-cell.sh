#!/usr/bin/env bash
#
# Runs unit + integration tests for a single compatibility-matrix cell.
# Rewrites .config/dotnet-tools.json with the cell's dotnet-ef version, then
# invokes restore/build/test with EF and Npgsql package versions overridden
# via MSBuild properties.
#
# This script mutates the working tree (.config/dotnet-tools.json). Callers
# that care about preserving the file should run it inside a fresh checkout
# (the matrix orchestrator and CI both do).
#
# Usage:
#   scripts/run-compat-cell.sh <efCoreVersion> <npgsqlEfCoreVersion> <dotnetEfToolVersion>
#
# Example:
#   scripts/run-compat-cell.sh 10.0.7 10.0.0 10.0.7
set -euo pipefail

if [[ $# -ne 3 ]]; then
  echo "usage: $0 <efCoreVersion> <npgsqlEfCoreVersion> <dotnetEfToolVersion>" >&2
  exit 64
fi

EF_VERSION="$1"
NPGSQL_VERSION="$2"
TOOL_VERSION="$3"

REPO_ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$REPO_ROOT"

echo "::group::cell setup ef=$EF_VERSION npgsql=$NPGSQL_VERSION tool=$TOOL_VERSION"

# Pin dotnet-ef to the cell's version.
manifest=".config/dotnet-tools.json"
if ! command -v jq >/dev/null 2>&1; then
  echo "jq is required" >&2
  exit 1
fi
tmp="$(mktemp)"
jq --arg v "$TOOL_VERSION" '.tools."dotnet-ef".version = $v' "$manifest" > "$tmp"
mv "$tmp" "$manifest"

# Clear obj/bin to make sure no stale assets from a prior cell linger.
find . -type d \( -name bin -o -name obj \) -prune -exec rm -rf {} + 2>/dev/null || true

dotnet tool restore
dotnet restore -p:EfCoreVersion="$EF_VERSION" -p:NpgsqlEfCoreVersion="$NPGSQL_VERSION"
dotnet build -c Release --no-restore \
  -p:EfCoreVersion="$EF_VERSION" -p:NpgsqlEfCoreVersion="$NPGSQL_VERSION"

echo "::endgroup::"

echo "::group::unit tests"
dotnet test tests/EntityFrameworkCore.ExtensibleMigrations.Tests/ \
  -c Release --no-build \
  -p:EfCoreVersion="$EF_VERSION" -p:NpgsqlEfCoreVersion="$NPGSQL_VERSION" \
  --logger "trx;LogFileName=unit-${EF_VERSION}.trx"
echo "::endgroup::"

echo "::group::integration tests"
dotnet test tests/integration/EntityFrameworkCore.ExtensibleMigrations.IntegrationTests/ \
  -c Release --no-build \
  -p:EfCoreVersion="$EF_VERSION" -p:NpgsqlEfCoreVersion="$NPGSQL_VERSION" \
  --logger "trx;LogFileName=integration-${EF_VERSION}.trx"
echo "::endgroup::"
