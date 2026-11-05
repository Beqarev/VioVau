# Project Architecture: VioVau

> See `docs/design.md` for game rules. This document covers how the code is organized.

## 1. Tech Stack

| | |
|---|---|
| Engine | Godot 4.x, .NET build |
| Language | C# 12 / .NET 8 |
| Tests | xUnit (core rules) |
| Networking | Godot high-level multiplayer (`ENetMultiplayerPeer`) first; Steam transport later |
| Data | JSON files in `/data` |
| Targets | Windows, macOS, Linux (desktop). Steam later. No web build (Godot 4 C# web export is not a priority). |

## 2. Guiding Principles

1. **Host-authoritative.** The host runs the rules. Clients send *intents* ("use card in slot 2", "interact with chest 14"), never results. The host validates, applies, and broadcasts.
2. **Rules live in plain C#.** Threat, cards, economy, dungeon generation, scoring live in `VioVau.Core`, which has **no Godot dependency**. It's unit-testable, deterministic (seeded), and can run headless for balancing simulations.
3. **Godot owns the physical world.** Movement, collision, enemy navigation, rendering, input, audio, and network transport live in the Godot project.
4. **Views never contain rules.** UI and visuals react to Core events; they never mutate game state directly.
5. **Multiplayer from day one.** Even solo play runs as a host with one player. There is no separate "single-player code path".

## 3. Who Owns What

| Concern | Lives in | Runs on |
|---|---|---|
| Dungeon generation (graph, room assignment, exits, loot tables) | Core | Host (seed shared with clients) |
| Threat meter, tiers, attribution, Strike targeting | Core | Host |
| Decks, hands, card effects (rule side) | Core | Host |
| Points, keys, loot bags, scoring | Core | Host |
| Player movement & collision | Godot | Owning peer (predicted), host reconciles |
| Enemy AI & navigation | Godot (reads Core state) | Host |
| Card effects (physical side: dash, stun radius) | Godot, triggered by Core events | Host, replicated |
| HUD, map, menus, animations, audio | Godot | Every peer locally |
| Meta profile (unlocks, passive cards, currency) | Core (model) + Godot (save IO) | Each player's own machine |

**The boundary:** Godot physics produces *facts* ("player 2 sprinted 1.4s", "player 1 entered room (3,4)", "enemy hit player 3") → fed to Core as commands → Core updates state and emits events → Godot reacts.

## 4. Players vs. Peers

Local couch co-op and online play mix, so these are separate concepts:

- **Peer** — one connected machine (Godot multiplayer peer id). The host is peer 1.
- **Player** — one human with a character. A peer owns **1–4 Players**.
- **LocalSlot** — a player's index on their own machine, mapped to an input device (gamepad N or keyboard+mouse).

Every game-state reference uses `PlayerId`, never peer id. Netcode maps `PlayerId → owning PeerId` for authority and RPC routing.

## 5. Networking

- **Transport:** `ENetMultiplayerPeer` (direct IP / LAN) for development. Steam lobbies/relay (GodotSteam or Facepunch.Steamworks — to evaluate) added later behind the same interface.
- **Client → Host:** RPCs carrying intents (`RequestUseCard(playerId, slot, target)`, `RequestInteract(playerId, entityId)`), plus input/position for owned players.
- **Host → Clients:**
  - Core events broadcast as compact messages (`ThreatChanged`, `CardDrawn`, `RoomRevealed`, `PlayerDowned`…).
  - Positions of players/enemies via `MultiplayerSynchronizer` (interpolated on remotes).
- **Prediction:** only for your own player's movement. Everything rule-related waits for the host (card plays get an immediate local "playing…" animation, confirmed or cancelled by the host).
- **Hidden information:** clients only receive what their players are allowed to know (other players' hands are hidden; unrevealed rooms aren't sent).
- **Determinism:** the dungeon seed is shared so clients can build room geometry locally, but the host's state is always the truth.

## 6. Core (`VioVau.Core`)

```
RunState            # the whole run: players, dungeon, threat, rng, phase
  Dungeon           # floors → room grid → rooms, exits (open/locked/sealed/…)
  ThreatSystem      # meter, tiers, per-player attribution, strike scheduling
  CardSystem        # decks, hands, discard, draw rules, effect resolution
  Economy           # points (carried/banked), keys, loot bags
  Scoring           # extraction, collapse, results
```

- **Commands in, events out.** `RunState.Apply(ICommand) → IReadOnlyList<IGameEvent>`. Commands are validated; invalid ones are rejected with a reason.
- **`Tick(TimeSpan dt)`** for time-based rules (passive Threat drift, draw timers, Collapse countdown).
- **Card effects are data.** A card = metadata + a list of effects: `[{ "type": "AddThreat", "amount": 2 }, { "type": "Stun", "radius": 4, "seconds": 3 }]`. Rule-side effects resolve in Core; physical effects are emitted as events for Godot to perform. New cards should rarely need new code.
- **Seeded RNG with separate streams** (`map`, `loot`, `cards`, `threat`, `ai`) so changing one system doesn't change another's results.
- **Serializable.** `RunState` can be saved/loaded (debugging, replays, future mid-run saves).

## 7. Godot Project (`/game`)

- **Autoloads** (keep few): `Net` (session, peers, player registry), `Session` (holds `RunState` on host / mirrored view on clients), `Profiles` (local meta saves), `Audio`.
- **Signals** are for view-side wiring. Core uses plain C# events; a `Session` adapter turns Core events into Godot signals for the UI.
- **Rooms** are hand-made scenes (`RoomTemplate`) with exit sockets N/E/S/W, spawn markers, and loot markers. The generator picks templates by room type; Godot instances them.
- **Camera:** one room per screen. Local players in the same room share a view; when they split up, the screen splits (SubViewports).

## 8. Data

- `/data/cards.json`, `enemies.json`, `rooms.json`, `threat.json`, `loot_tables.json`, `passives.json`.
- Loaded by Core at startup; schema-validated in tests (a broken card file fails CI, not a playtest).
- Balancing numbers live in data, not code.

## 9. Saves

- **Profile** (per local player): JSON, versioned (`"version": 1`) with migration on load. Stored in Godot `user://profiles/`.
- Meta progression is client-owned; cheating in a co-op game only hurts the cheater's own fun — acceptable for now.

## 10. Repository Layout

```
VioVau.sln
/src
  /VioVau.Core          # pure C# rules — no Godot references
  /VioVau.Core.Tests    # xUnit
  /VioVau.Sim           # (later) headless CLI: bots play runs for balancing
/game                   # Godot project (project.godot, res:// root)
  /assets               # sprites, fonts, audio
  /scenes
    /ui                 # HUD, menus, card views, map
    /rooms              # room templates, hub
    /entities           # player, enemies, chests, loot bag
  /scripts
    /net                # transport, RPC routing, player registry
    /session            # Core ↔ Godot adapter
    /entities           # player controller, enemy AI
    /ui                 # UI controllers (listen only)
    /input              # device → LocalSlot mapping
/data                   # JSON game data
/docs
```

The Godot project references `VioVau.Core` as a project reference so Core compiles into the game.

## 11. Conventions

- Core: no `using Godot;` ever — enforced by the project having no Godot reference.
- Game state is mutated only through `RunState.Apply`.
- IDs are typed (`PlayerId`, `RoomId`, `CardId`), not raw ints/strings.
- Every new rule gets a Core unit test.

## 12. Roadmap

| # | Milestone | Done when |
|---|---|---|
| M0 | Scaffold | Solution, Core + tests, empty Godot project building together |
| M1 | Two in a room | 2+ players (online + local) walk around a generated floor and move between rooms |
| M2 | Noise | Shared Threat, tiers, attribution HUD, loot & chests, keys/locked exits |
| M3 | Cards | Decks, hand of 3, draw rules, 12 cards |
| M4 | Danger | Enemies, hearts, downed/revive, death & loot bags, Strikes, Warden |
| M5 | Full run | Relics, extraction, Collapse, scoreboard → **vertical slice** |
| M6 | Hub & meta | Lobby hub, profiles, passive cards, saves |
| M7 | Content | More rooms, cards, enemies, floors |
| M8 | Look & feel | Pixel art, palette, audio, juice |
| M9 | Ship prep | Steam integration, settings, balancing via Sim |
