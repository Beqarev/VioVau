# tools/

| Script | What it does |
|---|---|
| `check.sh` | Build + tests, quiet. One line when green, only the errors when red. `--build` skips tests. |
| `playtest.sh` | Autopilot play-test with a summary (event counts, errors). Modes `solo`, `online` (default), `couch`, `brutal`; `--secs=N`, `--seed=N`, `--shots[=5,20,40]` (screenshots, needs a window). Port 7799. Full logs in `$TMPDIR/vv-playtest`. |
| `export.sh` | Windows/macOS/Linux release builds → `build/VioletVaults-*.zip`. **Verifies** each build has the game code and data; never hand out a build it didn't verify. |
| `hooks/pre-commit` | Git hook: runs `check.sh` before every commit. Install once: `git config core.hooksPath tools/hooks`. |
| `generate_audio.py` | Synthesizes the placeholder music/SFX (needs numpy). Loops crossfade one beat; music normalized to −2 dBFS. |
| `generate_card_art.py` | Draws the 32×32 card icons; then run `game/tools/ItemImporter.tscn` to palette-snap them. |
| `friends-readme.txt` | The `PLAYING.txt` shipped inside each release zip. |

Godot-side tools live in `game/tools/`: `SpriteBaker.tscn` (rigged `.glb` → sprite sheets, needs a window) and `ItemImporter.tscn` (2D items, headless OK). See `docs/art-bible.md`.

Override the Godot binary with `GODOT=/path/to/Godot`.
