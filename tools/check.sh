#!/usr/bin/env bash
# Build + test with quiet output: one line on success, only the errors on failure.
#   tools/check.sh          build and test
#   tools/check.sh --build  build only
set -uo pipefail
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
cd "$ROOT"
LOG="$(echo "${TMPDIR:-/tmp}" | sed "s:/*$::")/vv-check.log"

if ! dotnet build VioVau.sln -v quiet -nologo -clp:NoSummary >"$LOG" 2>&1; then
  echo "❌ build failed:"
  grep -E "error [A-Z]+[0-9]+" "$LOG" | sed -E -e "s#$ROOT/##" -e "s/ \[[^]]*\]$//" | sort -u | head -30
  exit 1
fi
warnings=$(grep -cE "warning [A-Z]+[0-9]+" "$LOG" || true)
[ "${1:-}" = "--build" ] && { echo "✅ build ok ($warnings warnings)"; exit 0; }

if ! dotnet test VioVau.sln --no-build -v quiet -nologo >"$LOG" 2>&1; then
  echo "❌ tests failed:"
  grep -E "^\s*(Failed |Error Message:|  Assert|   at VioVau)" "$LOG" | sed -E "s#$ROOT/##" | head -40
  grep -E "^(Failed|Passed)!" "$LOG"
  exit 1
fi
echo "✅ build ok ($warnings warnings) · $(grep -oE "Passed: +[0-9]+" "$LOG" | head -1 | tr -s ' ') tests"
