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
| `data/` | JSON game data (cards, enemies, rooms…). |
| `docs/` | Design and architecture. |

## Commands

```bash
dotnet build VioVau.sln
dotnet test VioVau.sln
```

Open `game/project.godot` in Godot and press F5 to run.
