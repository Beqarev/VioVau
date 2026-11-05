# VioVau — notes for Claude

Read `docs/design.md` (rules) and `docs/architecture.md` (code structure) before larger changes. Keep both docs updated when decisions change.

## Hard rules
- `src/VioVau.Core` must never reference Godot (`ArchitectureTests` enforces this). All game rules live there.
- Game state changes only through `RunState.Apply(ICommand)` and `RunState.Tick(dt)`. Commands are intents; results come back as `IGameEvent`s.
- Use `RngStreams` (per-system, seeded) for all randomness — never `System.Random`, `GD.Randi`, or `Guid` in rule code.
- Use typed IDs (`PlayerId`, `RoomId`, `CardId`), and `PlayerId` (not peer id) in game state.
- Balancing numbers go in `/data` JSON, not hard-coded.
- Every new rule gets a Core unit test.

## Commands
- Build: `dotnet build VioVau.sln`
- Test: `dotnet test VioVau.sln`
- The Godot project is `game/` (Godot 4.7 .NET, `Godot.NET.Sdk/4.7.2`). Godot is not on PATH on this machine.
