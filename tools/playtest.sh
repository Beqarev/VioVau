#!/usr/bin/env bash
# Automated play-test with autopilot bots; prints a short summary instead of the full logs.
#
#   tools/playtest.sh [solo|online|couch|brutal] [--secs=N] [--seed=N] [--shots[=5,20,40]]
#
#   solo    one host, one bot
#   online  host + client over 127.0.0.1 (default)
#   couch   one machine, two local bots (split screen)
#   brutal  online with 1 heart and aggressive Sentries (downed / revive / death)
#
# --shots takes screenshots (needs a visible window; uses --always-on-top) into $OUT/shots.
# Full logs stay in $OUT (default: $TMPDIR/vv-playtest). Uses port 7799 so it never clashes
# with a game the user is running on 7777. Run `dotnet build` (or tools/check.sh) first.
set -o pipefail   # no -u: macOS bash 3.2 treats empty arrays as unbound
ROOT="$(cd "$(dirname "$0")/.." && pwd)"
G="${GODOT:-/Applications/Godot_mono.app/Contents/MacOS/Godot}"
OUT="${OUT:-$(echo "${TMPDIR:-/tmp}" | sed "s:/*$::")/vv-playtest}"
MODE=online SECS=45 SEED=777 SHOTS=""
for a in "$@"; do
  case "$a" in
    solo|online|couch|brutal) MODE=$a ;;
    --secs=*) SECS=${a#*=} ;;
    --seed=*) SEED=${a#*=} ;;
    --shots) SHOTS="5,20,40" ;;
    --shots=*) SHOTS=${a#*=} ;;
    *) echo "unknown option: $a" >&2; exit 2 ;;
  esac
done
[ -x "$G" ] || { echo "❌ Godot not found at $G (set GODOT=...)"; exit 1; }
rm -rf "$OUT"; mkdir -p "$OUT/shots"

common=(--port=7799 --seed="$SEED")
host_extra=() godot_extra=(--headless)
if [ -n "$SHOTS" ]; then
  godot_extra=(--resolution 1440x810 --always-on-top)
  host_extra=(--screenshot-at="$SHOTS" --screenshot-dir="$OUT/shots")
fi
[ "$MODE" = brutal ] && common+=(--brutal)

pids=()
run() { local log=$1; shift; "$G" "$@" >"$OUT/$log" 2>&1 & pids+=($!); }
cleanup() { for p in "${pids[@]}"; do kill "$p" 2>/dev/null; done; wait 2>/dev/null; }
trap cleanup EXIT

case "$MODE" in
  solo)
    run host.log "${godot_extra[@]}" --path "$ROOT/game" -- --name=Host "${common[@]}" "${host_extra[@]}" --host --start-at=1 --autopilot ;;
  couch)
    run host.log "${godot_extra[@]}" --path "$ROOT/game" -- --name=Host "${common[@]}" "${host_extra[@]}" --host --wasd --arrows --start-at=2 --autopilot=all ;;
  online|brutal)
    run host.log "${godot_extra[@]}" --path "$ROOT/game" -- --name=Host "${common[@]}" "${host_extra[@]}" --host --start-at=2 --autopilot
    sleep 2
    run client.log --headless --path "$ROOT/game" -- --name=Guest "${common[@]}" --join=127.0.0.1 --autopilot ;;
esac

echo "▶ $MODE play-test, seed $SEED, ${SECS}s (logs: $OUT)"
sleep "$SECS"
cleanup; trap - EXIT

status=0
for log in "$OUT"/*.log; do
  name=$(basename "$log" .log)
  errors=$(grep -E "ERROR|SCRIPT ERROR|Exception|Unhandled|FATAL|❌" "$log" | grep -v "Screenshot" | sort | uniq -c | sort -rn)
  started=$(grep -c "entered" "$log" || true)
  echo
  echo "── $name ──"
  if [ "$started" -eq 0 ]; then echo "  ⚠️  no run activity (did the run start?)"; status=1; fi
  for key in "entered" "opened" "picked up key" "played" "hit by" "is DOWN" "revived" "DIED" "RELIC" "ESCAPED" "Threat tier" "COLLAPSE" "Run ended" "Rejected" "Ignored"; do
    n=$(grep -c "$key" "$log" || true)
    [ "$n" -gt 0 ] && printf "  %-14s %s\n" "$key" "$n"
  done
  grep -E "^\[Net\]" "$log" | tail -2 | sed 's/^/  /'
  if [ -n "$errors" ]; then
    echo "  ❌ errors:"; echo "$errors" | head -15 | sed 's/^/    /'; status=1
  fi
done
shots=$(ls "$OUT/shots" 2>/dev/null | wc -l | tr -d ' ')
[ -n "$SHOTS" ] && echo && echo "📸 $shots screenshot(s) in $OUT/shots"
echo
[ $status -eq 0 ] && echo "✅ play-test clean" || echo "❌ play-test found problems (full logs in $OUT)"
exit $status
