#!/usr/bin/env bash
#
# Regenerates the committed public-surface baselines (dogfooding the API surface gate):
#   tests/public-api/cli-help.txt     CLI help (root + every subcommand)
#   tests/public-api/mcp-tools.json   MCP tools/list snapshot
#   tests/public-api/public-api.txt   library public API (PublicApiGenerator)
#
# Run from anywhere. Commit the resulting diff.
set -eu

ROOT="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
OUT="$ROOT/tests/public-api"
mkdir -p "$OUT"

if ! command -v jq >/dev/null 2>&1; then
  echo "Error: 'jq' is required to regenerate the MCP tools baseline. Install it (e.g. apt install jq)." >&2
  exit 1
fi

echo ">>> Building CLI + MCP (Release) ..."
dotnet build "$ROOT/ChangeSharp.Cli/ChangeSharp.Cli.csproj" -c Release --nologo >/dev/null
dotnet build "$ROOT/ChangeSharp.Mcp/ChangeSharp.Mcp.csproj" -c Release --nologo >/dev/null

CLI="$ROOT/ChangeSharp.Cli/bin/Release/net10.0/ChangeSharp.Cli.dll"
MCP="$ROOT/ChangeSharp.Mcp/bin/Release/net10.0/ChangeSharp.Mcp.dll"

echo ">>> Generating cli-help.txt ..."
{
  LC_ALL=C dotnet "$CLI" --help
  for cmd in init new status validate release publish prerelease remove; do
    echo
    echo "##### $cmd #####"
    echo
    LC_ALL=C dotnet "$CLI" "$cmd" --help
  done
} > "$OUT/cli-help.txt"

echo ">>> Generating mcp-tools.json ..."
printf '%s\n' \
  '{"jsonrpc":"2.0","id":1,"method":"initialize","params":{}}' \
  '{"jsonrpc":"2.0","id":2,"method":"tools/list"}' \
  | dotnet "$MCP" 2>/dev/null \
  | tail -n 1 \
  | jq '.result.tools' > "$OUT/mcp-tools.json"

echo ">>> Generating public-api.txt ..."
CHANGESHARP_UPDATE_API_BASELINE=1 \
  dotnet test "$ROOT/ChangeSharp.Tests/ChangeSharp.Tests.csproj" \
    --filter "FullyQualifiedName~PublicApiBaselineTests" --nologo -v q >/dev/null

echo ">>> Done. Baselines written to $OUT"
echo ">>> Review: git diff tests/public-api/"