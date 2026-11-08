# Violet Vaults

A 1–4 player semi-cooperative, real-time dungeon extraction roguelike with cards. Online and couch co-op.

- Design: [docs/design.md](docs/design.md)
- Architecture: [docs/architecture.md](docs/architecture.md)

## Requirements

- .NET 8 SDK or newer
- Godot **4.7.x .NET** edition (the "Godot Engine - .NET" download, not the standard one)

## Layout

| Path | What |
|---|---|
| `src/VioVau.Core` | Game rules in plain C#. No Godot references. |
| `src/VioVau.Core.Tests` | xUnit tests for Core. |
| `game/` | Godot project (presentation, physics, input, networking). Open `game/project.godot`. |
| `game/data/` | JSON game data (dungeon, later cards/enemies…). |
| `docs/` | Design and architecture. |

## Commands

```bash
dotnet build VioVau.sln
dotnet test VioVau.sln
```

Open `game/project.godot` in Godot and press F5 to run.

## Playing / testing multiplayer

- **Couch:** in the menu, each person presses their join button: `Space` (WASD, Shift sprints), `Enter` (arrows, `.` sprints), or `A` on a gamepad (`B` to leave). Then **Host** → **Start run**.
- **Online:** one machine presses **Host** (UDP port 7777); others type the host's IP and press **Join**.
- **Two windows on one machine:** in the editor, *Debug → Customize Run Instances…*, enable 2 instances, and give them arguments:
  - instance 1: `-- --name=Host --host --start-at=2`
  - instance 2: `-- --name=Guest --join=127.0.0.1`

  The host starts automatically once 2 players are in. Click a window to give it keyboard focus.
- `Esc` in-game leaves the session.

### Dev flags

| Flag | Effect |
|---|---|
| `--name=X` | Player name |
| `--wasd` / `--arrows` | Add a local keyboard player |
| `--host` / `--join=IP` | Host or join immediately |
| `--start-at=N` | Host starts the run once N players are in |
| `--autopilot` | This machine's first player wanders the dungeon by itself |
| `--screenshot-at=2,8 --screenshot-dir=PATH` | Save screenshots at those seconds into the run |
