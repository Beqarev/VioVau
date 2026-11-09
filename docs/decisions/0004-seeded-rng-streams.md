# 0004 — Seeded per-system RNG streams

**Status:** Accepted (M0)

**Context:** Runs must be reproducible (bug reports, tests, `--seed=N` play-tests), and clients rebuild the dungeon from the seed.

**Decision:** All randomness in rule code comes from `RngStreams` (xoshiro256**), one stream per system (`Map`, `Loot`, `Cards`, `Threat`, `Ai`) derived from the run seed. Never `System.Random`, `GD.Randi`, `Guid`, or the clock in Core. Adding draws to one system doesn't shift the others.

**Consequences:** Same seed + same commands ⇒ same run. Cosmetic randomness in Godot (particles, footstep pitch) may use any RNG since it never affects rules.
