# 0002 — Rules engine in plain C#, no Godot

**Status:** Accepted (M0)

**Context:** Rules need to be fast to test, deterministic, and runnable headless (tests, future balancing sim, possibly a dedicated server).

**Decision:** All game rules live in `src/VioVau.Core`, a plain .NET library with no Godot reference. `ArchitectureTests` fails the build's tests if one appears. Godot (`game/`) handles rendering, audio, input, physics, networking — and things that need physics (enemy movement, positions) report results to Core as commands (`HitPlayer`, `MovePlayerToRoom`).

**Consequences:**
- 150+ unit tests run in seconds; rule bugs are caught without launching the game.
- Some duplication at the boundary (Godot computes positions/reach, Core validates).
- Enemy AI movement lives in Godot (`EnemyNode`), while which enemies exist, health and Strikes live in Core.
