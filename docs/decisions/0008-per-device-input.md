# 0008 — Per-device input instead of the InputMap

**Status:** Accepted (M1)

**Context:** Up to four local players on one machine (two keyboard halves, gamepads), mixable with online players.

**Decision:** Each local player is bound to an `InputDevice` (Keyboard WASD, Keyboard Arrows, Gamepad N) that is polled directly; `LocalPlayers` maps slots to devices. Game state uses `PlayerId`, never device or peer ids. Dev autopilot is just another `InputDevice`.

**Consequences:** Rebinding UI must be built on top of `InputDevice` rather than Godot's InputMap. Menus avoid keyboard focus so Space/Enter remain join keys. In `VioVau.Game.*` code write `Godot.Input` explicitly.
