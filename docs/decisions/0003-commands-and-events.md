# 0003 — Commands in, events out; RunView mirrors from events

**Status:** Accepted (M1)

**Context:** The same state must appear identically on every peer, and the host shouldn't render through a different path than clients.

**Decision:** State changes only through `RunState.Apply(ICommand)` and `RunState.Tick(dt)`. Each returns `IGameEvent`s (records, JSON-serialized via `[JsonDerivedType]` on `IGameEvent`). Every peer — host included — keeps a `RunView` built only from received events, and all visuals read the `RunView` or react to `NetSession.EventReceived`. Private information (hands, draws) uses `IPrivateEvent` and goes only to its owner.

**Consequences:**
- A new rule usually means: command + event + `RunView.Apply` + test.
- Late-loading scenes need state that events already described (e.g. `RunView.Enemies` keeps spawn records).
- Events are the network protocol: keep them small, and never put hidden info in public ones.
