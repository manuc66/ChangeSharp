#!/usr/bin/env bash
#
# Demo of the SemverPolicy.MaxImpact gate (the "max-impact cap").
#
# Run from anywhere:
#   samples/maximpact-gate/run-demo.sh
#
# It builds the ChangeSharp CLI from the repo, then exercises the gate inside
# this sample workspace. Exit code 0 = every check passed.
set -u

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO="$(cd "$ROOT/../.." && pwd)"
CLI_DLL="$REPO/ChangeSharp.Cli/bin/Release/net10.0/ChangeSharp.Cli.dll"

echo ">>> Building ChangeSharp CLI (Release) ..."
dotnet build "$REPO/ChangeSharp.Cli/ChangeSharp.Cli.csproj" -c Release --nologo >/dev/null || { echo "Build failed." >&2; exit 1; }

cd "$ROOT"

PASS=0
FAIL=0

# check <expected_exit> <label> <args...>
check() {
  local expected="$1"; shift
  local label="$1"; shift
  dotnet "$CLI_DLL" "$@" >/dev/null 2>&1
  local code=$?
  if [ "$code" -eq "$expected" ]; then
    printf '  PASS  %s (exit %s)\n' "$label" "$code"; PASS=$((PASS + 1))
  else
    printf '  FAIL  %s (exit=%s, expected=%s)\n' "$label" "$code" "$expected"; FAIL=$((FAIL + 1))
  fi
}

cleanup() {
  rm -f .changesharp/unreleased/*.md CHANGELOG.md
  rmdir .changesharp/unreleased .changesharp 2>/dev/null
}
trap cleanup EXIT
cleanup

echo "== Workspace: SemverPolicy.MaxImpact = minor =="
echo
echo "1. A Minor fragment is fine at creation time:"
check 0 "changesharp new --added (within cap)" new --added "Add a small feature"

echo
echo "2. A Major fragment is refused at creation time (exit 3 = validation):"
check 3 "changesharp new --breaking (blocked)" new --breaking "Break the API"

echo
echo "3. ... unless the author explicitly opts in with --allow-major:"
check 0 "changesharp new --breaking --allow-major (explicit opt-in)" new --breaking --allow-major "Break the API (approved)"

echo
echo "4. validate does not enforce the cap (it only checks format):"
check 0 "changesharp validate" validate

echo
echo "5. A real release is refused without the explicit flag (exit 3):"
check 3 "changesharp release (blocked)" release

echo
echo "6. ... and succeeds with --allow-major (the human-in-the-loop gate):"
check 0 "changesharp release --allow-major (explicit opt-in)" release --allow-major

echo
echo "7. The interactive menu marks blocked categories; --allow-major is exposed on 'new':"
dotnet "$CLI_DLL" new --help 2>/dev/null | grep -q -- '--allow-major' \
  && { echo "  PASS  new --help exposes --allow-major"; PASS=$((PASS + 1)); } \
  || { echo "  FAIL  new --help does not expose --allow-major"; FAIL=$((FAIL + 1)); }

echo
echo "==========================================="
echo "  $PASS passed, $FAIL failed"
echo "==========================================="
[ "$FAIL" -eq 0 ]