# Violet Vaults — agent guide

1–4 player **semi-co-op, real-time, top-down dungeon extraction roguelike** with cards (Godot 4.7 .NET, C#). Online (host-authoritative ENet) and couch co-op, mixable. Shared Threat, attributed noise, personal points; grab a Relic and escape before the Collapse.

**Start here:** `docs/STATUS.md` (where we are, what's next, known issues). Read more only when the task needs it:
`docs/design.md` (rules) · `docs/architecture.md` (code structure, roadmap) · `docs/art-bible.md` (art/audio specs) · `docs/decisions/` (why things are the way they are).
Folder rules load from nested files: `src/VioVau.Core/AGENTS.md`, `game/AGENTS.md`, `tools/README.md`.

## Commands
```bash
tools/check.sh                 # build + tests, quiet output (run before saying "done")
tools/playtest.sh online       # host + client autopilot run, prints a summary (also: solo, couch, brutal)
tools/export.sh                # release builds for friends (verifies them; never hand out anything else)
dotnet build VioVau.sln        # raw build
dotnet test VioVau.sln         # raw tests
godot-mono --path game         # play it yourself
```
Godot binary: `/Applications/Godot_mono.app/Contents/MacOS/Godot` (also `godot-mono`). After adding/changing assets: `godot-mono --headless --path game --import`.

## Hard rules
1. `src/VioVau.Core` never references Godot (`ArchitectureTests` enforces it). **All game rules live there.**
2. State changes only via `RunState.Apply(ICommand)` / `RunState.Tick(dt)`; results come back as `IGameEvent`s. New events get `[JsonDerivedType]` on `IGameEvent` (they go over the network).
3. Hidden info (hands) only in `IPrivateEvent`s — never in public events.
4. Randomness only via `RngStreams` — never `System.Random`, `GD.Randi`, `Guid` in rule code.
5. Typed IDs (`PlayerId`, `RoomId`, `CardId`…); `PlayerId`, not peer id, in game state.
6. Balance numbers live in `game/data/*.json`, not in code.
7. Every new rule gets a Core unit test.
8. The host decides; clients send intents and render from `RunView`. Anything visible/audible must also work on **clients**.
9. Keep docs in sync: update `docs/STATUS.md` when finishing work, add a `docs/decisions/` record for any lasting architecture choice.

## Map
| Area | Where |
|---|---|
| Rules engine | `src/VioVau.Core/` (`RunState*.cs`, `Threat/`, `Cards/`, `Enemies/`, `Dungeon/`, `Net/`) |
| Client mirror | `src/VioVau.Core/RunView.cs` |
| Tests | `src/VioVau.Core.Tests/` |
| Networking | `game/scripts/net/NetSession.cs` (autoload) |
| In-run scene | `game/scripts/session/GameSession*.cs` (partial class, one file per concern) |
| World / entities / UI | `game/scripts/{world,entities,ui}/` |
| Audio, settings, input, dev | `game/scripts/{audio,settings,input,dev}/` |
| Data | `game/data/{dungeon,threat,cards,enemies}.json` |
| Tools | `tools/` (scripts), `game/tools/` (SpriteBaker, ItemImporter) |

## Working style
- Plan briefly, then build. Verify by running: `tools/check.sh`, then `tools/playtest.sh` for gameplay changes, screenshots for anything visual. Don't claim something works without seeing it.
- Small focused commits with clear messages; the user pushes.
- Report in plain language: what changed, what was verified, what's left.
