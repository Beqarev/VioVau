#!/usr/bin/env bash
# Claude Code Stop hook: if C#/project/data files have uncommitted changes, run tools/check.sh.
# On failure, exit 2 so Claude sees the errors and keeps working (once — no loop).
input=$(cat)
echo "$input" | grep -q '"stop_hook_active": *true' && exit 0
root="$(cd "$(dirname "$0")/../.." && pwd)"
cd "$root" || exit 0
git status --porcelain | grep -qE '\.(cs|csproj|props|sln|json)$' || exit 0
out=$("$root/tools/check.sh" 2>&1) && exit 0
echo "$out" >&2
exit 2
