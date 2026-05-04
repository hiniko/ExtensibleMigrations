#!/usr/bin/env bash
#
# Iterates compat-matrix.json locally, invoking run-compat-cell.sh for each
# entry. Restores .config/dotnet-tools.json on exit so the working tree is
# left as it was found.
#
# Modes:
#   default        — run cells directly on the host (mutates .config/dotnet-tools.json
#                    and bin/obj between cells; restored on exit via git checkout).
#   --docker       — run each cell inside a fresh dotnet-sdk container with the host
#                    repo bind-mounted and the host docker socket forwarded so the
#                    Postgres testcontainer still works.
#   --only NAME    — run only the matrix entry whose .name matches.
#
# Usage:
#   scripts/run-compat-matrix.sh [--docker] [--only NAME] [path/to/compat-matrix.json]
set -euo pipefail

USE_DOCKER=0
ONLY=""
MATRIX_FILE=""

while [[ $# -gt 0 ]]; do
  case "$1" in
    --docker) USE_DOCKER=1; shift ;;
    --only)   ONLY="$2"; shift 2 ;;
    -h|--help)
      grep '^#' "$0" | sed 's/^# \{0,1\}//'
      exit 0
      ;;
    *)        MATRIX_FILE="$1"; shift ;;
  esac
done

REPO_ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$REPO_ROOT"

MATRIX_FILE="${MATRIX_FILE:-$REPO_ROOT/compat-matrix.json}"
if [[ ! -f "$MATRIX_FILE" ]]; then
  echo "matrix file not found: $MATRIX_FILE" >&2
  exit 66
fi

if ! command -v jq >/dev/null 2>&1; then
  echo "jq is required" >&2
  exit 1
fi

# Restore .config/dotnet-tools.json on exit (only if git is present and the
# file is tracked — otherwise leave it).
restore_manifest() {
  if git -C "$REPO_ROOT" ls-files --error-unmatch .config/dotnet-tools.json >/dev/null 2>&1; then
    git -C "$REPO_ROOT" checkout -- .config/dotnet-tools.json 2>/dev/null || true
  fi
}
trap restore_manifest EXIT

failed=()
passed=()

len=$(jq '.matrix | length' "$MATRIX_FILE")
for i in $(seq 0 $((len-1))); do
  cell=$(jq -c ".matrix[$i]" "$MATRIX_FILE")
  name=$(echo "$cell" | jq -r '.name')
  ef=$(echo "$cell"  | jq -r '.efCoreVersion')
  npg=$(echo "$cell" | jq -r '.npgsqlEfCoreVersion')
  tool=$(echo "$cell" | jq -r '.dotnetEfToolVersion')
  sdk=$(echo "$cell"  | jq -r '.dotnetSdkVersion // "10.0"')

  if [[ -n "$ONLY" && "$ONLY" != "$name" ]]; then
    continue
  fi

  echo
  echo "================================================================"
  echo "matrix cell: $name (ef=$ef, npgsql=$npg, dotnet-ef=$tool)"
  echo "================================================================"

  ok=0
  if [[ "$USE_DOCKER" == "1" ]]; then
    # Image tag uses the SDK major.minor; Microsoft publishes -alpine and -bookworm
    # variants. We pick the default Debian-based one.
    image="mcr.microsoft.com/dotnet/sdk:${sdk%%.x}"
    # Fresh container per cell. The repo is bind-mounted RW so jq edits to the
    # tool manifest survive into the test step inside the container; the EXIT
    # trap on this host script still restores it once the matrix completes.
    docker run --rm \
      -v "$REPO_ROOT:/work" \
      -v /var/run/docker.sock:/var/run/docker.sock \
      -w /work \
      "$image" \
      bash scripts/run-compat-cell.sh "$ef" "$npg" "$tool" \
      && ok=1 || ok=0
  else
    bash "$REPO_ROOT/scripts/run-compat-cell.sh" "$ef" "$npg" "$tool" \
      && ok=1 || ok=0
  fi

  if [[ "$ok" == "1" ]]; then
    passed+=("$name")
  else
    failed+=("$name")
  fi
done

echo
echo "================================================================"
echo "matrix summary"
echo "  passed: ${#passed[@]} (${passed[*]:-})"
echo "  failed: ${#failed[@]} (${failed[*]:-})"
echo "================================================================"

if [[ ${#failed[@]} -gt 0 ]]; then
  exit 1
fi
