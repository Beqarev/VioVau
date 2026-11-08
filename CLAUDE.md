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
- Every new rule gets a Core unit test.

## Commands
- Build: `dotnet build VioVau.sln`
- Test: `dotnet test VioVau.sln`
- The Godot project is `game/` (Godot 4.7 .NET, `Godot.NET.Sdk/4.7.2`). Godot binary: `/Applications/Godot_mono.app/Contents/MacOS/Godot` (also `godot-mono` on PATH).
- Automated play-test (run `dotnet build` first): host `--path game -- --host --start-at=2 --autopilot --screenshot-at=2,8 --screenshot-dir=<dir>`, client `--path game -- --join=127.0.0.1 --autopilot`. Add `--headless` for log-only runs. Logs print `[Net]`, `[Game]`, `[Dev]` lines.
