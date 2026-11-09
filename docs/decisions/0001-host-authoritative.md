# 0001 — Host-authoritative networking with intents

**Status:** Accepted (M1)

**Context:** 1–4 players online, mixable with couch players, semi-co-op with secret hands. No dedicated servers; a friend hosts.

**Decision:** One peer (the host) owns the only `RunState`. Clients send *intents* (`RequestInteract`, `RequestPlayCard` with a slot only, sprint flag, own position) and never decide outcomes. The host picks card targets and range-checks from its own positions, applies commands, and broadcasts the resulting events, filtered per peer (`EventVisibility.For`). Transport: ENet over UDP (port 7777 by default); Steam relay may come later behind the same interface.

**Consequences:**
- Cheating is limited to what the host allows; a modified host can do anything (fine for friends).
- Clients regenerate the map from the seed, so a modified client could see unrevealed rooms — accepted for co-op.
- Everything AI/host-side must be visible/audible on clients through synced state or events, never host-only code.
- Own-player movement is predicted locally; rule results wait for the host.
