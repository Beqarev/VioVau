# Workflow: turn a character .glb into sprites

Input: a rigged, animated `.glb` (e.g. from Sprixen Character Studio).

1. Put the file in `game/assets/raw/characters/<name>.glb` (Godot skips this folder via `.gdignore`).
2. Preview first (needs a window, not headless):
   `godot-mono --path game res://tools/SpriteBaker.tscn -- --model=assets/raw/characters/<name>.glb --out=assets/sprites/<name> --preview`
   Tune `--pitch` (camera angle, default 30), `--ortho` (zoom), `--center`, `--contrast` (dark armour needs ~2.0).
3. Bake: same command without `--preview`, plus `--anims=Idle:4,Walking_A:6,…` (animation:frames). It snaps to the 10-colour palette, adds the 1px outline and writes one sheet per animation (rows = down/left/right/up) + `sprites.json`.
4. `godot-mono --headless --path game --import`.
5. Wire it in `game/scripts/world/CharacterSprites.cs` if it's a new character type.
6. Look at the sheets (open the PNGs) and play-test with `--shots`: correct facing, no stray pixels, readable at 1× zoom.

Specs (sizes, palette, lighting): `docs/art-bible.md`.
