# 0007 — Sprites baked from 3D models

**Status:** Accepted (art pipeline)

**Context:** No pixel artist on the team. AI tools produce good rigged 3D characters (Sprixen Character Studio) but inconsistent 2D sprite sheets.

**Decision:** Animated characters are generated as rigged `.glb` models and baked by `game/tools/SpriteBaker.tscn` into 4-direction sprite sheets, snapped to the strict 10-colour palette with a 1px outline. 2D items go through `game/tools/ItemImporter.tscn` for the same palette/outline treatment. Raw sources live in `game/assets/raw/` (ignored by Godot).

**Consequences:** Consistent style and any number of animations for free; baking needs a window. Check each generator's licence allows commercial use before shipping.
