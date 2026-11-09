# game/ — the Godot 4.7 .NET project

Godot renders, plays sound, reads input, moves bodies and does networking. It does **not** decide rules — those are in `src/VioVau.Core`.

## Structure
- Autoloads: `GameSettings` (user://settings.json), `Audio` (buses, music layers, SFX), `NetSession` (ENet host/client, event broadcast).
- `scripts/session/GameSession*.cs` — the in-run scene, a partial class: `GameSession.cs` (lifecycle, per-frame), `.Setup` (world + HUD), `.Host` (physical facts → commands, revives), `.Targeting`, `.Events` (visuals + audio + hearing), `.Enemies`.
- Visuals react to `NetSession.EventReceived`; state is read from `RunView`. The host sends commands; clients send intents (`Request*`).

## Conventions
- In `VioVau.Game.*` namespaces write `Godot.Input` explicitly (`VioVau.Game.Input` is a namespace).
- Design resolution 480×270, integer scaling; SplitScreen sub-viewports render at physical resolution.
- **Text:** use `UiFonts.Regular` (Silkscreen, very wide). Lay out by measuring (`font.GetStringSize`) — never hard-coded x offsets. World-space labels use `Lights.Unshaded` so the darkness doesn't eat them.
- **HUD zones** (design px) stay clear of world decoration: top-left Threat panel ~215×45, top-right minimap (x>400, y<56), bottom-left hand (x<200, y>205), bottom-right room label. Also keep doorway lanes and loot spots clear.
- Highlights/tooltips go on a separate child node with a higher ZIndex (see `LootNode`), so items stay behind players.
- **Audio:** world sounds through `Audio.PlaySfxAt` (fades by room distance via `Audio.Hearing`). Anything AI/host-driven must also make sound on **clients** — derive it from synced state or events, not host-only code.
- Colours from `ui/Palette.cs` (strict 10-colour palette, see `docs/art-bible.md`).
- After adding/changing assets: `godot-mono --headless --path game --import`.

## Verify
`tools/playtest.sh online` after gameplay changes; add `--shots` for screenshots after anything visual, and check split-screen (`couch`) too.
