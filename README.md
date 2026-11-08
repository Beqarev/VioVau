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
- **In a run:** sprinting, opening chests and unlocking doors make noise and raise the shared Threat. Keys are picked up by walking over them; Vault doors need one (or a Lockpick). Hollow Sentries hunt what they see and hear; the louder you are, the more the dungeon sends after you. At 0 hearts you're downed: a teammate stands beside you and holds Interact (1.5s), or plays Rally.
- **Winning:** lift a Relic in the Relic Chamber (the room furthest from the Lobby), get back to the Lobby's rune circle and press Interact to **escape** and bank everything you carry. The first escape starts a 90s **Collapse** — anyone still inside when it ends is lost. Most points banked wins. Host presses `Enter` on the scoreboard for a rematch.

| | Move | Sprint | Interact | Cards (slots 1–3) |
|---|---|---|---|---|
| Keyboard WASD | `WASD` | `Shift` | `E` | `1` `2` `3` |
| Keyboard Arrows | arrows | `.` | `/` | `K` `L` `;` (or numpad `1` `2` `3`) |
| Gamepad | stick / d-pad | `RB` / `RT` | `A` | `X` `Y` `B` |
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
| `--autopilot=all` | Every local player on this machine is an autopilot (couch tests) |
| `--brutal` | 1 heart, fast sharp-eyed Sentries, strikes every 8s — to test downed / death / bags quickly |
| `--seed=N` | Host uses this dungeon seed (reproducible runs) |
| `--port=N` | Host/join on this UDP port instead of 7777 |
| `--auto-rematch` | Host starts the next run 3s after the scoreboard |
| `--autopilot` | This machine's first player plays by itself: loots chests, grabs keys, unlocks Vaults (key or Lockpick), takes Library/Merchant cards, plays a card every few seconds, sprints everywhere |
| `--screenshot-at=2,8 --screenshot-dir=PATH` | Save screenshots at those seconds into the run |
