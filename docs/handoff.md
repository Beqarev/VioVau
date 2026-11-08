# Handoff — continuing Violet Vaults

Read this first, then `CLAUDE.md`, `docs/design.md`, `docs/architecture.md`, `docs/art-bible.md`.

## The game (one paragraph)
Violet Vaults is a 1–4 player **semi-co-op, real-time, top-down dungeon extraction roguelike** (Godot 4.7 .NET / C#). Online (host-authoritative ENet) and couch co-op, mixable. Players loot a generated vault; every noisy action raises a **shared Threat** but noise is **attributed** per player, and the dungeon's Strikes and the Warden hunt the loudest. Points are personal: grab a Relic, escape through the Lobby before the Collapse seals everyone else in; most banked points wins. Cards are one-time abilities (hand of 3, secret hands).

## Where it stands
Done and committed: M0 scaffold · M1 dungeon/networking/couch · M2 Threat/loot/keys · Art pipeline · M3 cards (16) · M4 enemies/health/revive/death · M5 relics/extraction/Collapse/scoreboard/rematch · **Look pass 1** (hi-res rendering, lighting + shadows, particles, camera slides, new menu). **153 Core tests pass.** The vertical slice is complete and playable.

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

### 1. Builds for friends + pause/settings
- Export presets for **Windows, macOS, Linux** (`game/export_presets.cfg` is gitignored — commit a template or document it). Make sure `game/data/*.json` is included (export filter `*.json`). One zip per platform.
- **Pause / Esc menu** in a run: Resume, Settings, Leave run. Esc currently leaves immediately; move that into the menu.
- **Settings**: fullscreen/windowed, master/music/SFX volume (saved to `user://settings.json`).
- A short "playing with friends" section in the README (Tailscale/ZeroTier, or forward UDP 7777).

### 2. Audio
- `Audio` autoload with buses **Master / Music / SFX** (volumes from settings).
- **SFX** react to events in `GameSession.OnEvent` (and local input): footsteps (walk/sprint), chest open, key pickup, door unlock, card play (per category), noise ring, Sentry notice/wind-up/hit, player hurt/downed/revived/died, relic lift, escape, tier rise, Collapse alarm + last-15s ticking, UI hover/click. Positional (`AudioStreamPlayer2D`) for world sounds; louder noises should *sound* louder — noise is the core mechanic.
- **Music**: three exploration layers (Calm / Restless / Hunting) in the **same key and tempo**, crossfaded by Threat tier; a Warden sting/layer; a Collapse track; a menu track. Loop points clean.
- Files: `game/assets/audio/music/*.ogg` (loops), `game/assets/audio/sfx/*.wav` (short, normalized, mono for positional). Raw/unprocessed sources go in `game/assets/raw/` (ignored by Godot).
- Prompts and the music brief are in `docs/art-bible.md` §5 Batch 5.

### 3. Look pass 2
- Rooms: 3/4 **wall front faces** (north wall gets a visible face; walls feel tall), ambient-occlusion shading along walls, **props per room type** (Library shelves + lecterns, Merchant counter + rug, Guard Post weapon racks, Vault gold piles, Relic Chamber pillars + rune floor, Echo Chamber hanging bells, Hush Room carpets). Keep collision simple (props mostly decorative; any solid prop needs a collider and must not block doorway lanes or loot spots).
- Optional real tileset (16×16) generated to the palette (see art bible) replacing the code-drawn floor/walls in `RoomNode`.
- A **pixel font** (OFL-licensed, e.g. *Silkscreen*, *Pixelify Sans*, *m5x7*) for HUD/menus; keep sizes readable at 480×270 design scale.
- Card art (32×32 per card, art bible Batch 3) shown in `HandHud` and on lecterns.

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
