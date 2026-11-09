# 0005 — Data-driven balance in game/data/*.json

**Status:** Accepted (M2)

**Context:** Balance will be tuned constantly by play-testing with friends; recompiling for every number is slow and buries tuning in code.

**Decision:** Numbers live in `game/data/{dungeon,threat,cards,enemies}.json` (comments allowed). The host loads them into `GameConfig` and sends it in `RunStarted`, so clients always use the host's numbers. Config records in Core have sensible defaults so tests don't need the files.

**Consequences:** New tunables go into a config record + the JSON, never as literals in rule code. A rematch re-reads the files, so the host can tweak between runs.
