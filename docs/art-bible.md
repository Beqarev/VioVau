# Violet Vaults — Art & Audio Bible

How every asset should look and sound, and the exact prompts to generate them (e.g. in Sprixen).
**Rule #1: consistency beats quality.** One mediocre-but-consistent set looks better than ten beautiful assets in ten styles.

---

## 1. Technical specs (non-negotiable — the code depends on these)

| Thing | Spec |
|---|---|
| Base resolution | 480×270, scaled up by whole numbers (×4 = 1080p). Never smooth/blurred scaling. |
| Grid | **16×16 px tiles**. One room ≈ 30×16 tiles (one screen). |
| View | **Top-down 3/4** (like *Enter the Gungeon* / *Zelda: A Link to the Past*): we see the top and front of things. Walls show their front face. |
| Light | From **top-left**. Shadows fall bottom-right. |
| Outline | 1 px outline in **Void `#0d0716`** on characters, enemies, items. Tiles: no outline. |
| Anti-aliasing | **None.** Hard pixel edges only. No gradients, no blur, no glow halos baked in (glow is added in-engine). |
| Background | **Transparent** for everything except tiles. |
| Animation | 4 directions (down, up, left, right — right can be a mirror of left). Frame counts below. |
| Format | PNG sprite sheets, frames in a row, equal frame size, no padding unless stated. |

### Sizes
| Asset | Frame size |
|---|---|
| Player characters | 24×24 (body ~14×18 inside) |
| Small enemies | 24×24 |
| Big enemies / miniboss | 32×32 |
| The Warden | 48×48 |
| Chests, Relics | 16×16 |
| Keys, small pickups, UI icons | 16×16 canvas (item ~10 px) |
| Card illustrations | 32×32 (drawn inside a 48×64 card frame) |
| Tiles | 16×16 |

---

## 2. Palette (strict — 10 colors)

Files: [`game/assets/palette/`](../game/assets/palette/) — `violet-vaults.hex`, `violet-vaults.gpl` (Aseprite/GIMP), and `violet-vaults-swatch.png` (**upload this as a reference image** when the tool allows).

| Name | Hex | Use |
|---|---|---|
| Void | `#0d0716` | Outlines, deepest shadow, darkness |
| Shadow | `#1d1030` | Floor, dark areas |
| Deep | `#2a1a45` | Floor details, shadowed sides |
| Stone | `#4b2a7a` | Walls, furniture |
| Violet | `#6a3fa8` | Wall highlights, cloth |
| Amethyst | `#8b5cc8` | Accents, magic, UI |
| Mist | `#7d6a9e` | Desaturated details, metal, dust |
| Lavender | `#c8a2ff` | Highlights, player trims |
| Pale | `#e3c7ff` | Bright highlights |
| Bone | `#f4f0ff` | **Value & danger only**: loot, keys, Relics, enemy eyes, attack tells, text |

**White means "look here".** Treasure and threats are the only things that get Bone/Pale. Everything else lives in the purples.

AI tools won't hit these colors exactly — that's fine. The import pipeline snaps every pixel to the nearest palette color. What matters is **value contrast and silhouette**.

---

## 3. Mood

Tense, quiet, mysterious. Thieves creeping through an ancient sealed vault that is *waking up*. Think: candlelight in a cathedral crypt, amethyst crystals, dust, old gold that reads as white. Not cute, not gory. Readable silhouettes over detail.

---

## 4. The style block (paste at the start of EVERY prompt)

```
Pixel art game asset, top-down 3/4 view like Enter the Gungeon, 16x16 pixel grid, hard pixel edges, no anti-aliasing, no gradients, no blur, limited palette of 10 colors only: #0d0716 #1d1030 #2a1a45 #4b2a7a #6a3fa8 #8b5cc8 #7d6a9e #c8a2ff #e3c7ff #f4f0ff (deep purples, void black, stark white highlights), 1 pixel dark outline #0d0716, light from top-left, dark mysterious dungeon vault atmosphere, transparent background.
```

Then add the asset-specific part below.

### Two kinds of assets

| Kind | Tool | What you hand over | What happens |
|---|---|---|---|
| **Characters & enemies** (anything that moves) | Sprixen **Character Studio** (3D) | the `.glb` file | The **sprite baker** renders it into pixel-art sheets: every animation × 4 directions, snapped to the palette, outlined. Consistent by construction, no extra credits per animation. |
| **Everything else** (tiles, doors, chests, icons, cards, VFX) | Sprixen **Create Sprite / 2D Studio** | the `.png` | Snapped to the palette and sliced on import. |

Character Studio models come rigged with ~76 animations (Idle, Walking, Running, Interact, PickUp, Use_Item, Hit, Death, Dodge, Cheer…), so one generation = a fully animated character.
**Player colors (P1–P4) are done in code** — no need to generate variants.

Bake command (run from the repo root):
```
/Applications/Godot_mono.app/Contents/MacOS/Godot --path game res://tools/SpriteBaker.tscn -- --model=assets/raw/characters/delver.glb --out=assets/sprites/delver
```
Add `--preview` to render one large test image first; tune `--pitch` (camera angle, default 30), `--ortho` (zoom), `--center`, `--contrast`, `--anims=Idle:4,Walking_A:6,…`.

---

## 5. Prompts by batch (in the order we need them)

### Batch 1 — Style anchor (needed now; M3–M4)

**A1. The Delver (player character)** — Character Studio ✅ **done** (`game/assets/raw/characters/delver.glb`, baked to `game/assets/sprites/delver/`)
```
[STYLE BLOCK] A hooded vault thief called a Delver: slim figure in a dark violet cloak with a deep hood, face hidden in shadow except two small pale eyes, small lantern hanging at the hip giving a tiny white glow, leather satchel, soft boots. Sneaky, agile, not armored. Sprite sheet, 24x24 frames: idle (2 frames) and walk (4 frames) for down, up, left, right. Readable silhouette at small size.
```
~~Color variants~~ — not needed; player colors are applied in code.

**A2. Dungeon tileset** — *made in code instead* (seamless tiles don't come out of single-sprite/pack generators reliably). Prompt kept for reference:
```
[STYLE BLOCK] Top-down dungeon tileset for an ancient sealed amethyst vault, 16x16 tiles in one sheet: dark stone floor (4 variants, subtle cracks), floor with faint rune engraving (2 variants), wall top tiles and wall front-face tiles showing carved stone blocks with violet highlights (straight, inner corner, outer corner), doorway opening in a wall, small rubble and dust decals, amethyst crystal cluster decoration growing from wall base. Seamless, tileable, no characters.
```

**A3. Doors**
```
[STYLE BLOCK] Three 48x16 dungeon doors seen top-down 3/4 to fit a 3-tile doorway: 1) heavy locked vault door, dark iron bands, one glowing white keyhole in the center; 2) the same door swung open; 3) a sealed void door covered in glowing purple runes with a white eye symbol.
```

**A4. Chests & loot** — ✅ **done** via Sprixen *Pack mode → Items / Props* (8 items, 2 credits). Raw in `game/assets/raw/items/`, imported to `game/assets/sprites/items/` with:
`Godot --headless --path game res://tools/ItemImporter.tscn -- --in=assets/raw/items --out=assets/sprites/items` (`--size` = longest side in px, default 18).

```
[STYLE BLOCK] Treasure set, 16x16 each: small wooden-and-iron chest closed, same chest open and empty, ornate vault chest closed with white trim, same ornate chest open revealing white glowing coins, a single white key (standard key), a black-and-violet key with a glowing white eye (void key), a small pile of white coins. Treasure items use bright white #f4f0ff so they stand out.
```

### Batch 2 — Danger (M4)

*Batch 2 characters: use **Character Studio** (3D), same as the Delver; the baker turns them into sprites.*

**B1. Patrol — "Hollow Sentry"** (walks a route) — ✅ **done**: Character Studio, base *Knight* (2 credits). Baked with:
`Godot --path game res://tools/SpriteBaker.tscn -- --model=assets/raw/characters/hollow_sentry.glb --out=assets/sprites/hollow_sentry --contrast=2.0 --anims=Idle:4,Walking_A:6,Running_A:6,1H_Melee_Attack_Chop:5,Hit_A:3,Death_A:6,Block:4`
(Dark armor needs `--contrast=2.0` to stay readable. Glowing white visor eyes are added in code.)

Character Studio prompt used (pick the closest base, then describe):
```
Hollow vault guard: an empty suit of ancient armor with no person inside, dark purple and black weathered armor with violet highlights, visor slit glowing with two small bright white eyes, faint violet mist leaking from the joints, tattered dark violet cape, dark iron sword and dark shield with a violet eye emblem. Menacing, eerie, no skin visible.
```
Original 2D prompt (unused):
```
[STYLE BLOCK] Small enemy, 24x24 frames: an empty suit of ancient vault-guard armor animated by violet mist, no face, two white glowing eye-points in the helmet slit, carrying a short halberd. Animations: idle (2), walk (4) in 4 directions, attack wind-up (2, eyes flare white), attack (2).
```
**B2. Sentry — "Watcher"** (stationary, sees in a cone)
```
[STYLE BLOCK] Stationary enemy, 24x24 frames: a floating stone eye set in a carved amethyst frame, single large white iris, faint violet runes around it. Animations: idle float (4), look-around turn (4), alert (2, iris turns bright white and wide).
```
**B3. The Warden** (unkillable hunter, Threat tier 4)
```
[STYLE BLOCK] Large boss enemy, 48x48 frames: a towering gaunt guardian of the vault, long cloak of darkness, elongated skull-like helm with a crown of amethyst crystals, one huge white eye, long thin arms with claws, drifting rather than walking. Terrifying but readable. Animations: drift (4) in 4 directions, scream/roar (3), grab attack (3).
```
**B4. Relics** (the run's big prize)
```
[STYLE BLOCK] Three legendary relic items, 16x16 each, glowing bright white with violet accents: an ornate crown, a jeweled chalice, a crystal skull. Each on a small dark pedestal.
```

### Batch 3 — Cards & UI (M3) ✅ (Look pass 2)

In-game: `tools/generate_card_art.py` → `assets/raw/cards/` → ItemImporter → `assets/sprites/cards/` (32×32). HUD via `CardArt` / `HandHud`. UI font: OFL Silkscreen in `assets/fonts/`.

**C1. Card frame**
```
[STYLE BLOCK] Trading-card frame for a pixel art card game, 48x64 pixels, dark violet border with carved stone corners, empty 32x32 art window at the top, empty text area at the bottom, a small circular slot in the top-right corner for a noise number. Plus 3 variants of the border for rarity: common (stone), rare (amethyst), legendary (white-gold trim).
```
**C2. Card illustrations** — one prompt each, 32×32, same reference image:
```
[STYLE BLOCK] 32x32 card illustration icon: <SUBJECT>. Simple, bold, readable at small size, centered.
```
| Card | `<SUBJECT>` |
|---|---|
| Dash | a cloaked figure blurring forward with motion lines |
| Hush | a finger raised to hooded lips, small "shh" sound waves fading |
| Lockpick | two thin lockpicks inside a keyhole |
| Flash Powder | a small pouch bursting into a white flash |
| Lantern | an old lantern casting light rays outward |
| Rally | a hand pulling another hand up |
| Blink | a figure dissolving into violet particles and reappearing |
| Scapegoat | a hooded figure pointing at another figure, sound waves moving between them |
| Pickpocket | a gloved hand lifting a white key from a pocket |
| Loot Magnet | white coins flying toward an open hand |
| Gilded Greed | an overflowing chest with white coins spilling |
| Smoke Veil | a figure vanishing into thick purple smoke |
| Void Sight | an eye with a violet iris seeing through a wall |
| Decoy | a small clockwork noisemaker emitting sound rings |
| Shatter | a cracked armored helmet with a white crack of light |
| Shared Fortune | two hands each holding a white coin |

**C3. UI icons** (16×16 each)
```
[STYLE BLOCK] UI icon set, 16x16 each, simple and bold: heart full, heart empty, white coin (points), standard key, void key, noise/sound waves icon, skull, eye, exit door with arrow, hourglass.
```

### Batch 4 — Effects (feel pass)

```
[STYLE BLOCK] VFX sprite sheets, transparent background: 1) footstep dust puff (4 frames, 16x16); 2) chest opening burst of white sparkles (6 frames, 32x32); 3) door unlock — keyhole flash and swirling violet runes (6 frames, 32x32); 4) noise ring — expanding broken violet sound ring (6 frames, 48x48); 5) Threat surge — a dark violet pulse wave with white sparks (8 frames, 64x64); 6) key pickup glint (4 frames, 16x16).
```
(Lights, darkness, flicker and screen shake are done in code.)

### Batch 5 — Music ✅ (procedural; regenerate with `python3 tools/generate_audio.py`)

Shared music style: `Dark ambient dungeon soundtrack, slow and tense, mysterious, low drones, distant choir pads, music box and glass harmonica motifs, sparse percussion, no vocals with words, loopable, for a stealth heist dungeon game.`

| Track | Add to the style | Length |
|---|---|---|
| Hub / lobby | `calm and warm but eerie, candlelit sanctuary before the heist, gentle music box melody` | 2 min loop |
| Exploration — Calm | `very quiet, breathing drones, occasional distant chimes, lots of space` | 3 min loop |
| Exploration — Restless | `same key and tempo as the calm layer, add a low heartbeat pulse and creeping strings` | 3 min loop |
| Exploration — Hunting | `same key and tempo, add tense ostinato strings and metallic percussion` | 3 min loop |
| The Warden | `menacing, deep brass stabs, dissonant choir, pounding slow drums` | 2 min loop |
| Collapse (escape) | `urgent fast escape, driving drums, rising strings, crumbling stone feel, 140 bpm` | 2 min loop |
| Victory / extracted | `short triumphant but bittersweet sting` | 10 s |
| Death | `short descending dark sting, music box winding down` | 6 s |

**Important:** generate the three Exploration layers in the **same key and tempo** (say it explicitly, e.g. "D minor, 70 bpm") — the game crossfades them as the Threat rises.

### Sound effects
Retro sound effects (footsteps, chest open, key pickup, door unlock, sprint, tier rise, hit, pickup coin, UI clicks) can be **synthesized in code**, which fits the pixel style and keeps them consistent. If Sprixen has an SFX tool, use the style: `retro 16-bit game sound effect, dark dungeon, short, clean`.

---

## 6. In the game (status)

| Asset | Where it's used |
|---|---|
| Delver | `PlayerCharacter` — idle / walking / running × 4 directions from `assets/sprites/delver` (loaded by `CharacterSprites` from `sprites.json`). P1–P4 are told apart by a colored ring under the feet + number. |
| Chests, keys | `LootNode` — wooden chests normally, ornate vault chests inside Vaults, keys bob. |
| Floor, walls, crystals, Lobby circle | Drawn in code by `RoomNode` (deterministic per room). Swap for a real tileset later without touching game logic. |
| Hollow Sentry, coins, crown, void key | Baked/imported, waiting for M4 (enemies) and M5 (Relics). |

After adding or re-baking art, run `Godot --headless --path game --import` once so Godot picks up the new files.

## 7. Handing assets over

Drop raw downloads into `game/assets/raw/<kind>/` with clear names (`characters/warden.glb`, `tiles/vault.png`, `music/explore_calm.ogg`). `game/assets/raw/` is ignored by Godot (it has a `.gdignore`); only processed output in `game/assets/sprites/` etc. ships. The import step will:
1. Characters: bake the `.glb` into palette-snapped, outlined sheets + `sprites.json`.
2. 2D art: snap every pixel to the 10-color palette, check frame sizes, slice sheets.
3. Wire them into the game.

**Licensing:** before we ship anything (Steam, itch), confirm the platform's terms allow **commercial use** of generated assets on your plan — free tiers often don't.
