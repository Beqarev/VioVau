# Handoff — continuing Violet Vaults

Read this first, then `CLAUDE.md`, `docs/design.md`, `docs/architecture.md`, `docs/art-bible.md`.

## The game (one paragraph)
Violet Vaults is a 1–4 player **semi-co-op, real-time, top-down dungeon extraction roguelike** (Godot 4.7 .NET / C#). Online (host-authoritative ENet) and couch co-op, mixable. Players loot a generated vault; every noisy action raises a **shared Threat** but noise is **attributed** per player, and the dungeon's Strikes and the Warden hunt the loudest. Points are personal: grab a Relic, escape through the Lobby before the Collapse seals everyone else in; most banked points wins. Cards are one-time abilities (hand of 3, secret hands).

## Where it stands
Done and committed: M0 scaffold · M1 dungeon/networking/couch · M2 Threat/loot/keys · Art pipeline · M3 cards (16) · M4 enemies/health/revive/death · M5 relics/extraction/Collapse/scoreboard/rematch · Look pass 1 · **pause/settings/exports** · **Audio** · **Look pass 2** (3/4 wall faces, AO, room props, Silkscreen, card art). **153 Core tests pass.** The vertical slice is complete and playable.

## Non-negotiable architecture rules
- Game rules live in `src/VioVau.Core` (plain C#, **no Godot** — a test enforces it). Godot (`game/`) does physics, rendering, input, audio, networking.
- State changes only via `RunState.Apply(ICommand)` / `RunState.Tick(dt)`; results are `IGameEvent`s (register new ones with `[JsonDerivedType]` on `IGameEvent`). Hidden info (hands) uses `IPrivateEvent`.
- The **host** applies commands; clients send intents. Every peer renders from `RunView` (built from events). Visuals react to `NetSession.EventReceived`.
- Randomness only via `RngStreams`. Balance numbers in `game/data/*.json`. Typed IDs.
- Every new rule gets a Core unit test. Keep the docs updated when decisions change.

## Commands
```bash
dotnet build VioVau.sln
dotnet test VioVau.sln
godot-mono --path game                      # play (menu → Play solo)
/Applications/Godot_mono.app/Contents/MacOS/Godot --headless --path game --import   # after adding/changing assets
```

### Automated play-test (do this after gameplay/visual changes)
```bash
G=/Applications/Godot_mono.app/Contents/MacOS/Godot
$G --path game --resolution 1440x810 --always-on-top -- --name=Host --port=7799 --seed=777 --host --start-at=2 --autopilot --screenshot-at=5,20,40 --screenshot-dir=/tmp/vv &
$G --path game -- --name=Guest --port=7799 --join=127.0.0.1 --autopilot &
```
- Logs print `[Net]`, `[Game 12.3s]`, `[Dev]` lines; grep them for errors and events.
- Use `--port=7799` (the user may be running the game on 7777). Use `--always-on-top` for screenshots (macOS stops drawing covered windows).
- Other dev flags: `--autopilot=all` (couch bots), `--brutal` (1 heart, aggressive Sentries), `--auto-rematch`, `--menu-shot`, `--wasd/--arrows`, `--seed=N`. See README.

## Map of the code
| Area | Files |
|---|---|
| Rules | `src/VioVau.Core/RunState*.cs` (Cards, Danger, Ending), `Threat/`, `Cards/`, `Enemies/`, `Dungeon/` |
| Client mirror | `src/VioVau.Core/RunView.cs` |
| Networking | `game/scripts/net/NetSession.cs` (autoload) |
| In-run scene | `game/scripts/session/GameSession.cs` |
| World visuals | `game/scripts/world/` (RoomNode, LootNode, Lights, Fx, RoomNavigator, WorldLayout) |
| Entities | `game/scripts/entities/` (PlayerCharacter, EnemyNode) |
| UI | `game/scripts/ui/` (MainMenu, HUDs, HandHud, Scoreboard, SplitScreen) |
| Input | `game/scripts/input/` (InputDevice, LocalPlayers) |
| Dev/tools | `game/scripts/dev/` (Autopilot, DevOptions), `game/tools/` (SpriteBaker, ItemImporter) |
| Data | `game/data/{dungeon,threat,cards,enemies}.json` |

## Next tasks, in order

### 1. Builds for friends + pause/settings ✅
- Export presets: `game/export_presets.cfg.example` (copy → gitignored `export_presets.cfg`). `*.json` included; macOS needs ETC2/ASTC. Builds land in `build/VioletVaults-{windows,macos,linux}.zip`.
- **Pause / Esc menu** in a run: Resume, Settings, Leave run (`PauseMenu` — freezes the world for solo/couch; online peers keep simulating).
- **Settings**: fullscreen + Master/Music/SFX volumes via `GameSettings` autoload → `user://settings.json`. Also on the main menu.
- README: "Playing with friends" (Tailscale/ZeroTier / UDP 7777) + export steps.

### 2. Audio ✅
- `Audio` autoload + Master/Music/SFX buses (volumes from `GameSettings`).
- **SFX** wired in `GameSession` / player / enemies / UI: footsteps, chest, key, door, card-by-category, noise ring (volume scales with noise amount), Sentry notice/wind-up/hit, hurt/downed/revived/died, relic, escape, tier rise, Collapse alarm + last-15s ticks, UI hover/click. World sounds use `AudioStreamPlayer2D`.
- **Music**: D minor / 70 bpm explore layers (Calm → Restless → Hunting) crossfaded by Threat; Warden layer; Collapse (140 bpm); menu; victory/death stings. Regenerated via `python3 tools/generate_audio.py`.
- Files: `game/assets/audio/music/*.ogg`, `game/assets/audio/sfx/*.wav`; raw wavs in `game/assets/raw/audio/`.

### 3. Look pass 2 ✅
- Rooms: 3/4 **wall front faces** (`WallFaceHeight` on north), floor AO bands, **decorative props** (no collision) per type — Library shelves, Merchant counter + rug, Guard racks + training ring, Vault gold piles, Relic pillars + rune ring, Echo bells, Hush carpets (`RoomNode`).
- OFL **Silkscreen** in `game/assets/fonts/` via `UiFonts` (HUD + menus; AA off).
- **Card art** 32×32 (palette-snapped via `ItemImporter`) in `assets/sprites/cards/`; shown in `HandHud` + lecterns (`CardArt`). Regen: `python3 tools/generate_card_art.py` then ItemImporter.

### 4. M6 Hub & meta-progression, then 5. M7 content
See `docs/design.md` §10 and §8 and the roadmap in `docs/architecture.md`. Ideas already decided: meta currency = 100% of banked points on escape / 25% of carried on death; unlocks over raw stats; per-player local profiles in `user://profiles/`. M7: more floors + stairs, the Watcher (stationary eye), elite Sentries, tier effects (faster patrols, shifting doors), a player shove/stun, Shrines, Trap Halls, Void Doors with minibosses.

## Asset generation rules
- **Palette (strict, 10 colours):** `#0d0716 #1d1030 #2a1a45 #4b2a7a #6a3fa8 #8b5cc8 #7d6a9e #c8a2ff #e3c7ff #f4f0ff`. White (`#f4f0ff`) only for value and danger. Files in `game/assets/palette/`.
- Pixel art, top-down 3/4 view, light from top-left, 1px dark outline, no anti-aliasing, transparent background. Sizes: tiles 16×16, players/Sentries 32×32 frames, Warden 48×48, items ~16×16, card art 32×32.
- **Characters** (anything animated): generate a rigged **3D `.glb`** and bake it with `game/tools/SpriteBaker.tscn` (needs a window). **2D items:** `game/tools/ItemImporter.tscn` (headless OK). Both snap to the palette and outline automatically. Then run `--import`.
- Check each tool's licence allows **commercial use** before anything ships.

## Working style the user expects
- Plan briefly, then build; verify by running the game (autopilot + screenshots) — don't claim something works without seeing it.
- Small, focused commits with clear messages per milestone (the user pushes).
- Explain results in plain language: what changed, what was verified, what's left.

## Lessons from the first Cursor pass (reviewed and fixed)
- **Never ship an unverified build.** Use `tools/export.sh` — it fails loudly if a build lacks the game code (`data_VioVau_*`) or the data pack is tiny. A previous export produced a 74 KB pack and opened to nothing.
- **Don't commit tool leftovers** (`.nuget-packages/`, `.tmp/`, `tools/__pycache__/`) or regenerable raw audio (`game/assets/raw/audio/`). All are gitignored now. Don't disable `NuGetAudit` — that was a sandbox workaround.
- **Lay out text by measuring it** (`font.GetStringSize`), never with hard-coded x offsets: the pixel font (Silkscreen) is much wider than the default font. Cards have optional `shortName` in `cards.json` for tight spaces (`CardStyle.FitName`).
- **Keep props and decoration out of HUD zones**: top-left Threat panel (~215×45 design px), top-right minimap (x > 400, y < 56), bottom-left hand (x < 200, y > 205), bottom-right room label. Also out of doorway lanes and loot spots.
- **Highlight/tooltip drawing goes on a separate layer** (`LootNode`'s tooltip child), so the item itself stays at floor level behind players.
- **Audio:** regenerate with `python3 tools/generate_audio.py` (loops are crossfaded by one beat and music is normalized to −2 dBFS). World sounds go through `Audio.PlaySfxAt`, which fades by room distance via `Audio.Hearing` (set by `GameSession`). Anything the AI does must also produce sounds on **clients** (derive them from synced state, not host-only logic).
- **Verify visually** with screenshots after any UI/art change (`--always-on-top`, `--port=7799`); check split-screen and online (host + client), not just solo.
