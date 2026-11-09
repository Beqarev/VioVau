# Workflow: review uncommitted changes (or another agent's work)

1. `git status` and `git diff --stat` for scope; read the diff file by file (don't dump it all at once).
2. Check against the hard rules in `AGENTS.md`: no Godot in Core, state only via commands/events, events registered, hidden info private, `RngStreams`, typed IDs, numbers in `game/data`, a test per new rule.
3. Check the game conventions in `game/AGENTS.md`: measured text, HUD zones, client-side sounds/visuals, `Godot.Input`.
4. Look for leftovers: debug prints, disabled checks, tool caches (`.nuget-packages/`, `__pycache__`, `.tmp/`), regenerable raw assets, secrets.
5. Run `tools/check.sh`, then `tools/playtest.sh online` (and `--shots` if anything visual changed) — look at the screenshots.
6. Report findings ranked by severity with file:line, then what you verified. Fix only if asked.
