# Workflow: play-test a change

1. `tools/check.sh` — must be green first.
2. Pick the mode that exercises the change:
   - gameplay / networking → `tools/playtest.sh online`
   - downed / revive / death / bags → `tools/playtest.sh brutal`
   - split screen, local input, HUD per player → `tools/playtest.sh couch`
   - quick smoke → `tools/playtest.sh solo --secs=20`
3. Anything visual (UI, art, lighting, layout): add `--shots` (or `--shots=5,20,40`) and **look at the screenshots** in `$TMPDIR/vv-playtest/shots/`. Check text fits, nothing sits under HUD zones, nothing draws over players.
4. Read the summary: events you expected should have non-zero counts; any `❌ errors` must be explained or fixed. Full logs: `$TMPDIR/vv-playtest/*.log` (grep, don't dump them).
5. Report what ran, what you saw, and anything not verified.

Reproduce a specific dungeon with `--seed=N`. Never use port 7777 (the user may be playing).
