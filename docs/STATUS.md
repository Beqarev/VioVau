# Status — Violet Vaults

Living document: **update it when you finish a piece of work** (what's done, what's next, new known issues). Rules and commands are in `AGENTS.md`; this file is only "where are we".

_Last updated: 2026-11-09_

## Done
M0 scaffold · M1 dungeon / networking / couch · M2 Threat / loot / keys · art pipeline · M3 cards (16) · M4 enemies / hearts / downed / revive / death bags · M5 relics / extraction / Collapse / scoreboard / rematch · Look pass 1 (hi-res rendering, lighting, particles, camera) · pause + settings + verified exports · audio (placeholder synth) · Look pass 2 (3/4 wall faces, AO, room props, Silkscreen font, card art) · AI tooling (AGENTS.md, check/playtest scripts, workflows, hooks, decision records).

**The vertical slice is complete and playable** solo, couch and online. 153 Core tests pass.

## Next, in order
1. **M6 Hub & meta-progression** — lobby hub between runs, per-player local profiles in `user://profiles/`, meta currency (100% of banked points on escape, 25% of carried on death), unlocks over raw stats, passive cards. See `docs/design.md` §10.
2. **M7 Content** — more floors + stairs, the Watcher (stationary eye), elite Sentries, tier effects (faster patrols, shifting doors), player shove/stun, Shrines, Trap Halls, Void Doors with minibosses. See `docs/design.md` §8.
3. **Real music** to replace the synthesized placeholders (`tools/generate_audio.py`), same file names under `game/assets/audio/music/`.
4. M8 look & feel polish, M9 ship prep (Steam, balancing sim) — see the roadmap in `docs/architecture.md`.

## Known issues / loose ends
- Music and SFX are synthesized placeholders.
- The pause menu hasn't been clicked through visually in a play-test.
- `game/assets/fonts/raw/` duplicates the Silkscreen font; can be removed after checking nothing references it.
- `.nuget-packages/` (≈177 MB, git-ignored) is a leftover cache in the repo folder; safe to delete.
- Map is regenerated from the seed on clients, so a modified client could see unrevealed rooms (accepted for co-op; see `docs/decisions/0001-host-authoritative.md`).

## Lessons learned (don't repeat these)
- An export once shipped a 74 KB data pack with no game code. Only `tools/export.sh` builds go to friends.
- The pixel font is much wider than the default font: hard-coded x offsets overlapped everywhere. Measure text.
- Props once sat under the HUD and highlights drew over players. Respect HUD zones; tooltips on their own layer.
- Host-only AI code produced no sounds on clients. Derive client effects from synced state/events.
- macOS stops drawing covered windows, which froze screenshots: use `--always-on-top` (`playtest.sh --shots` does).
- Don't disable `NuGetAudit` or commit tool caches.
