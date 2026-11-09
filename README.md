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

## Playing

From the menu: **Play solo**, **Host a game** (friends join with your IP), or **Join a game**. More players on the same machine press `Space` (WASD), `Enter` (arrows) or `A` (gamepad) on the menu before you start.

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
- `Esc` in-game opens the pause menu (Resume / Settings / Leave run). Settings (fullscreen, volumes) also live on the main menu and save to `user://settings.json`.

### Playing with friends (online)

Godot talks over **UDP** (default port **7777**). Friends need a reachable IP:

1. **Easiest:** put everyone on the same [Tailscale](https://tailscale.com/) or [ZeroTier](https://zerotier.com/) network and use the host's VPN IP in Join.
2. **Same LAN:** use the host's local IP (`ipconfig` / System Settings → Network).
3. **Over the internet without a VPN:** the host forwards **UDP 7777** on their router to their PC, then shares their public IP. (CGNAT / many mobile ISPs can't do this — use Tailscale instead.)

Firewall tip: allow inbound UDP 7777 for the game (or Godot) on the host.

### Export builds (Windows / macOS / Linux)

```bash
tools/export.sh
```

Builds all three platforms into `build/`, **verifies** each one (game code present, data pack a sane size — an export without them opens to nothing) and zips them with a `PLAYING.txt` for friends (`tools/friends-readme.txt`: how to open unsigned builds, Tailscale / port 7777, controls). Needs Godot 4.7 .NET export templates, either installed via *Editor → Manage Export Templates* or unpacked into `build/godot-templates/`. The first run creates `game/export_presets.cfg` from `game/export_presets.cfg.example` (the real file is gitignored).

Builds aren't code-signed: on macOS friends right-click → Open the first time; on Windows "More info → Run anyway".
