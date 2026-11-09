# VioVau.Core — the rules engine

Plain C# (net8+), **no Godot**. Everything that decides what happens in a run lives here; Godot only shows it.

## How a rule is added
1. **Command** (player intent) in `Commands.cs` → handled in `RunState.Apply` (or the matching partial: `RunState.Cards.cs`, `.Danger.cs`, `.Ending.cs`). Reject invalid intents with a reason, never throw.
2. **Event** (what happened) — a record implementing `IGameEvent`, registered with `[JsonDerivedType(typeof(X), "x")]` on `IGameEvent`. If only the owner may see it (hand contents), implement `IPrivateEvent` and check `Net/EventVisibility`.
3. **RunView** applies the event so every peer's mirror stays correct (`RunView.Apply`).
4. **Numbers** go in a config record (`GameConfig`, `ThreatConfig`, `CardsConfig`, `EnemiesConfig`, `DungeonConfig`) with defaults, loaded from `game/data/*.json`.
5. **Test** in `src/VioVau.Core.Tests/` — use `TestRuns.Started(...)`, `TestRuns.WalkTo(...)`, `TestRuns.QuietThreat`. Tests may set internal state directly (`player.CarriedPoints = 10`).

## Rules
- Randomness: `RngStreams` only (one stream per system, seeded from the run seed). Same seed + same commands ⇒ same run.
- Typed IDs from `Ids.cs`. Time comes in via `Tick(dt)` — never read a clock.
- Keep events small and serializable (they're broadcast as JSON via `Net/EventCodec`).
- `ArchitectureTests` fails if anything here references Godot.

Run `tools/check.sh` (or `dotnet test VioVau.sln`) after any change.
