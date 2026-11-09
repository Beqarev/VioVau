# VioVau — notes for Claude

Read `docs/design.md` (rules) and `docs/architecture.md` (code structure) before larger changes. Keep both docs updated when decisions change.

## Hard rules
- `src/VioVau.Core` must never reference Godot (`ArchitectureTests` enforces this). All game rules live there.
- Game state changes only through `RunState.Apply(ICommand)` and `RunState.Tick(dt)`. Commands are intents; results come back as `IGameEvent`s.
- Use `RngStreams` (per-system, seeded) for all randomness — never `System.Random`, `GD.Randi`, or `Guid` in rule code.
- Use typed IDs (`PlayerId`, `RoomId`, `CardId`), and `PlayerId` (not peer id) in game state.
- Balancing numbers go in `game/data/*.json`, not hard-coded.
- New `IGameEvent` types must be registered with `[JsonDerivedType]` on `IGameEvent` (they're sent over the network).
- In `VioVau.Game.*` namespaces write `Godot.Input` explicitly (`VioVau.Game.Input` is a namespace).
- Every new rule gets a Core unit test. Tests may set internal state directly (`InternalsVisibleTo`), e.g. `player.CarriedPoints = 10`.
- Events only the owning player may see implement `IPrivateEvent`; never put hidden info (hands) in public events.

- Release builds: `tools/export.sh` (exports + verifies + zips). Never hand out a build it didn't verify.
- UI text: measure with `font.GetStringSize`; never hard-code x offsets (pixel font is wide). Keep world decoration out of HUD zones (see `docs/handoff.md`).

## Commands
- Build: `dotnet build VioVau.sln`
- Test: `dotnet test VioVau.sln`
- The Godot project is `game/` (Godot 4.7 .NET, `Godot.NET.Sdk/4.7.2`). Godot binary: `/Applications/Godot_mono.app/Contents/MacOS/Godot` (also `godot-mono` on PATH).
- Art pipeline: see `docs/art-bible.md`. Characters = `.glb` from Sprixen Character Studio → `res://tools/SpriteBaker.tscn` (needs a window). 2D items → `res://tools/ItemImporter.tscn` (headless OK). Then `--headless --import`. Raw sources live in `game/assets/raw/` (has `.gdignore`).
- Automated play-test (run `dotnet build` first): host `--path game -- --host --start-at=2 --autopilot --screenshot-at=2,8 --screenshot-dir=<dir>`, client `--path game -- --join=127.0.0.1 --autopilot`. `--seed=N` makes the dungeon reproducible; use `--port=7799` so tests don't clash with a game the user is running on 7777; pass Godot `--always-on-top` before `--` for screenshot runs (macOS stops drawing covered windows, which freezes screenshots); `--auto-rematch` loops runs; `--brutal` (1 heart, aggressive Sentries) and `--autopilot=all` (couch bots) exercise downed/revive/death; log lines carry the run time (`[Game 12.3s]`). Add `--headless` for log-only runs. Logs print `[Net]`, `[Game]`, `[Dev]` lines.
